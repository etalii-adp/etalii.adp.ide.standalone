using EtAlii.Adp.Backend.Hierarchy;
using EtAlii.Adp.Backend.Projects;
using EtAlii.Adp.Common;
using EtAlii.Adp.Common.Wire;
using EtAlii.Adp.Diagram.Databricks;
using EtAlii.Adp.Diagram.Wire;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here
using Path = EtAlii.Adp.Common.Wire.Path;

namespace EtAlii.Adp.Backend.Tests;

/// <summary>
/// The Databricks arc over the real host: one configuration file serving three diagram types,
/// each opening and streaming its own reading; a reposition landing in the <c>.adp</c>'s
/// <c>layout:</c> block while the body never changes by a byte; and edits and undo travelling
/// the whole way (databricks-diagrams Requirements 1, 7 and the integration halves of 2 and 12).
/// </summary>
/// <remarks>
/// The halves worth proving here rather than in the module's own tests: that three
/// registrations onto ONE body each stream a different projection, that the <c>resource:</c>
/// header selects over the wire, and that layout persistence holds end to end - move, reopen,
/// position survives, undo returns the registration byte for byte with the body untouched
/// throughout.
/// </remarks>
public class DatabricksFlowTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private const string DeveloperUsername = "admin";
    private const string DeveloperCredential = "changeme";
    private const string SessionTokenHeader = "session-token";
    private static readonly TimeSpan MessageTimeout = TimeSpan.FromSeconds(20);

    private const string Lakehouse = """
        bundle:
          name: lakehouse

        resources:
          jobs:
            nightly:
              name: Nightly
              tasks:
                - task_key: ingest
                  notebook_task:
                    notebook_path: notebooks/ingest
                - task_key: transform
                  depends_on:
                    - task_key: ingest
                  notebook_task:
                    notebook_path: notebooks/transform
          pipelines:
            bronze:
              name: Bronze
              catalog: lakehouse_dev
              schema: bronze
              libraries:
                - notebook:
                    path: transformations/bronze

        targets:
          dev:
            mode: development
            default: true
        """;

    private readonly WebApplicationFactory<Program> _factory;
    private readonly string _appDataRoot;
    private readonly string _projectFolder;

    public DatabricksFlowTests(WebApplicationFactory<Program> baseFactory)
    {
        _appDataRoot = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.IntegrationTests", Guid.NewGuid().ToString("N"));
        _projectFolder = IoPath.Combine(_appDataRoot, "sample-project");
        Directory.CreateDirectory(_projectFolder);

        // One body, three registrations - the family's whole routing story in one folder.
        File.WriteAllText(IoPath.Combine(_projectFolder, "lakehouse.yml"), Lakehouse);
        File.WriteAllText(IoPath.Combine(_projectFolder, "lakehouse.adp"), "databricks/bundle\nbody: lakehouse.yml\n");
        File.WriteAllText(IoPath.Combine(_projectFolder, "lakehouse.job.adp"), "databricks/job\nbody: lakehouse.yml\n");
        File.WriteAllText(
            IoPath.Combine(_projectFolder, "lakehouse.pipeline.adp"),
            "databricks/pipeline\nbody: lakehouse.yml\nresource: bronze\n");

        _factory = baseFactory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("developer");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IProjectStore>();
                services.AddSingleton<IProjectStore>(new FileProjectStore(_appDataRoot));
                services.RemoveAll<Problems.IProblemStore>();
                services.AddSingleton<Problems.IProblemStore>(provider => new Problems.ProblemStore(
                    _appDataRoot,
                    provider.GetRequiredService<DiagramFileRouter>(),
                    provider.GetRequiredService<Common.DiagramValidators>()));
            });
        });
    }

    public void Dispose()
    {
        _factory.Dispose();
        TestFolder.TryDelete(_appDataRoot);

        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task OneBody_StreamsThreeReadings_OnePerRegistration()
    {
        // Arrange: Requirement 1.4 - three MIME types over one file, each registration opening
        // its own projection; the pipeline one selected by its resource: header (Requirement 1.3).
        using var channel = CreateChannel();
        var headers = await LoginAsync(channel);
        var projectId = await AddProjectAsync(channel, headers);

        // Act.
        var bundle = await BaselineAsync(channel, headers, projectId, "lakehouse.adp");
        var job = await BaselineAsync(channel, headers, projectId, "lakehouse.job.adp");
        var pipeline = await BaselineAsync(channel, headers, projectId, "lakehouse.pipeline.adp");

        // Assert.
        Assert.Contains(bundle, element => element.Id.Value == "bundle");
        Assert.Contains(bundle, element => element.Id.Value == "resource:jobs/nightly");
        Assert.Contains(bundle, element => element.Id.Value == "target:dev");
        Assert.Contains(job, element => element.Id.Value == "task:ingest" && element.Type == "databricks/job+task");
        Assert.Contains(job, element => element.Id.Value == "edge:ingest->transform");
        Assert.Contains(pipeline, element => element.Id.Value == "pipeline");
        Assert.Contains(pipeline, element => element.Id.Value == "library:transformations/bronze");
        Assert.Contains(pipeline, element => element.Id.Value == "flow:pipeline->target");
    }

    [Fact]
    public async Task AReposition_LandsInTheAdp_SurvivesReopen_AndUndoReturnsIt_WithTheBodyUntouchedThroughout()
    {
        // Arrange: the layout-persistence promise (Requirement 7), end to end. The diagram must
        // be open on the SAME connection, because core will not edit a document a caller is not
        // looking at.
        using var channel = CreateChannel();
        var client = new DiagramService.DiagramServiceClient(channel);
        var contextClient = new ContextService.ContextServiceClient(channel);
        var headers = await LoginAsync(channel);
        var projectId = await AddProjectAsync(channel, headers);
        var watchId = ShortGuid.NewShortGuid();
        var bodyPath = IoPath.Combine(_projectFolder, "lakehouse.yml");
        var adpPath = IoPath.Combine(_projectFolder, "lakehouse.job.adp");
        var bodyBefore = await File.ReadAllBytesAsync(bodyPath, TestContext.Current.CancellationToken);
        var adpBefore = await File.ReadAllTextAsync(adpPath, TestContext.Current.CancellationToken);

        using var cts = CreateMessageTimeout();
        using var call = client.Open(
            new OpenDiagramRequest { ProjectId = projectId, WatchId = watchId, Path = PathOf("lakehouse.job.adp") },
            headers, cancellationToken: cts.Token);
        await ReadAddAsync(call.ResponseStream, cts.Token);

        // Act: drag the ingest task to an authored spot.
        var moved = await client.MoveElementAsync(
            new MoveElementRequest
            {
                ProjectId = projectId,
                WatchId = watchId,
                Path = PathOf("lakehouse.job.adp"),
                ElementId = "task:ingest",
                Position = new Point2D { X = 300, Y = 180 },
            },
            headers, cancellationToken: TestContext.Current.CancellationToken);

        // Assert: the position is in the registration, and nowhere near the body (7.3, 7.7).
        Assert.Equal("", moved.Error);
        Assert.Contains("layout:", await File.ReadAllTextAsync(adpPath, TestContext.Current.CancellationToken), StringComparison.Ordinal);
        Assert.Equal(new RegistrationPosition(300, 180), RegistrationLayout.Read(adpPath)["task:ingest"]);
        Assert.Equal(bodyBefore, await File.ReadAllBytesAsync(bodyPath, TestContext.Current.CancellationToken));

        // Act, continued: a fresh open sees the stored position overlaid (7.4).
        var reopened = await BaselineAsync(channel, headers, projectId, "lakehouse.job.adp");
        var ingest = reopened.Single(element => element.Id.Value == "task:ingest");
        Assert.Equal((300d, 180d), (ingest.Position.X, ingest.Position.Y));

        // Act, continued: one undo returns the registration byte for byte (7.6).
        var undone = await ExecuteProjectActionAsync(contextClient, projectId, watchId, headers, HistoryContextActionProvider.UndoActionId);

        // Assert.
        Assert.True(undone.Accepted, undone.Error);
        Assert.Equal(adpBefore, await File.ReadAllTextAsync(adpPath, TestContext.Current.CancellationToken));
        Assert.Equal(bodyBefore, await File.ReadAllBytesAsync(bodyPath, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ARunIfEdit_TravelsTheSelectionChain_AndIsOneUndoAway()
    {
        // Arrange: the property path over the real selection chain - which also proves a
        // databricks element resolves while every other module's resolver is registered beside
        // this one (Requirement 10.5, 12's integration half).
        using var channel = CreateChannel();
        var contextClient = new ContextService.ContextServiceClient(channel);
        var headers = await LoginAsync(channel);
        var projectId = await AddProjectAsync(channel, headers);
        var watchId = ShortGuid.NewShortGuid();
        var entries = await EntriesAsync(channel, headers, projectId, watchId);
        var bodyPath = IoPath.Combine(_projectFolder, "lakehouse.yml");
        var before = await File.ReadAllTextAsync(bodyPath, TestContext.Current.CancellationToken);

        var selected = await contextClient.SelectAsync(
            new SelectRequest
            {
                ProjectId = projectId,
                WatchId = watchId,
                Selection = ElementChain(entries["lakehouse.job.adp"], "task:transform"),
            },
            headers, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Empty(selected.Error);

        // Act.
        var result = await contextClient.SetPropertyAsync(
            new SetPropertyRequest
            {
                ProjectId = projectId,
                WatchId = watchId,
                PropertyId = DatabricksContextPropertyProvider.RunIfProperty,
                Value = "ALL_DONE",
            },
            headers, cancellationToken: TestContext.Current.CancellationToken);

        // Assert.
        Assert.True(result.Accepted, result.Error);
        Assert.Contains("run_if: ALL_DONE", await File.ReadAllTextAsync(bodyPath, TestContext.Current.CancellationToken), StringComparison.Ordinal);

        // Act, continued: one undo returns the body byte for byte.
        var undone = await ExecuteProjectActionAsync(contextClient, projectId, watchId, headers, HistoryContextActionProvider.UndoActionId);

        // Assert.
        Assert.True(undone.Accepted, undone.Error);
        Assert.Equal(before, await File.ReadAllTextAsync(bodyPath, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task APipelineScalarEdit_ThroughItsOwnRegistration_ReachesTheSharedBody()
    {
        // Arrange: the same body edited through the pipeline registration - the shared-store
        // half of Requirement 1.4: three diagram types, one document, one history.
        using var channel = CreateChannel();
        var contextClient = new ContextService.ContextServiceClient(channel);
        var headers = await LoginAsync(channel);
        var projectId = await AddProjectAsync(channel, headers);
        var watchId = ShortGuid.NewShortGuid();
        var entries = await EntriesAsync(channel, headers, projectId, watchId);
        var bodyPath = IoPath.Combine(_projectFolder, "lakehouse.yml");
        var before = await File.ReadAllTextAsync(bodyPath, TestContext.Current.CancellationToken);

        var selected = await contextClient.SelectAsync(
            new SelectRequest
            {
                ProjectId = projectId,
                WatchId = watchId,
                Selection = ElementChain(entries["lakehouse.pipeline.adp"], "pipeline"),
            },
            headers, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Empty(selected.Error);

        // Act.
        var result = await contextClient.SetPropertyAsync(
            new SetPropertyRequest
            {
                ProjectId = projectId,
                WatchId = watchId,
                PropertyId = $"{DatabricksContextPropertyProvider.PipelineScalarPrefix}catalog",
                Value = "lakehouse_prod",
            },
            headers, cancellationToken: TestContext.Current.CancellationToken);

        // Assert.
        Assert.True(result.Accepted, result.Error);
        Assert.Contains("catalog: lakehouse_prod", await File.ReadAllTextAsync(bodyPath, TestContext.Current.CancellationToken), StringComparison.Ordinal);

        var undone = await ExecuteProjectActionAsync(contextClient, projectId, watchId, headers, HistoryContextActionProvider.UndoActionId);
        Assert.True(undone.Accepted, undone.Error);
        Assert.Equal(before, await File.ReadAllTextAsync(bodyPath, TestContext.Current.CancellationToken));
    }

    /// <summary>The baseline elements of one open diagram, on a fresh connection.</summary>
    private async Task<IReadOnlyList<Element>> BaselineAsync(
        GrpcChannel channel,
        Metadata headers,
        ShortGuid projectId,
        string fileName)
    {
        var diagramClient = new DiagramService.DiagramServiceClient(channel);
        using var cts = CreateMessageTimeout();
        using var call = diagramClient.Open(
            new OpenDiagramRequest { ProjectId = projectId, WatchId = ShortGuid.NewShortGuid(), Path = PathOf(fileName) },
            headers, cancellationToken: cts.Token);
        return await ReadAddAsync(call.ResponseStream, cts.Token);
    }

    private static async Task<IReadOnlyList<Element>> ReadAddAsync(
        IAsyncStreamReader<Delta> stream,
        CancellationToken cancellationToken)
    {
        try
        {
            while (await stream.MoveNext(cancellationToken))
            {
                if (stream.Current.ActionCase == Delta.ActionOneofCase.Add)
                {
                    return [.. stream.Current.Add.Elements];
                }
            }
        }
        catch (RpcException exception) when (exception.StatusCode == StatusCode.Cancelled)
        {
            // Reading stopped at the deadline; the caller's assertion reports the empty baseline.
        }
        catch (OperationCanceledException)
        {
            // Same story, thrown the other way.
        }

        return [];
    }

    /// <summary>
    /// Every entry of the project, by name - one nested level deep, because a registration is a
    /// child of the subject it names.
    /// </summary>
    private static async Task<IReadOnlyDictionary<string, Common.Wire.ShortGuid>> EntriesAsync(
        GrpcChannel channel,
        Metadata headers,
        ShortGuid projectId,
        ShortGuid watchId)
    {
        var hierarchyClient = new HierarchyService.HierarchyServiceClient(channel);
        var response = await hierarchyClient.ListEntriesAsync(
            new ListEntriesRequest { ProjectId = projectId, WatchId = watchId },
            headers, cancellationToken: TestContext.Current.CancellationToken);

        var byName = response.Entries.Entries_.ToDictionary(entry => entry.Name, entry => entry.Id, StringComparer.Ordinal);
        foreach (var parent in response.Entries.Entries_.Where(entry => entry.HasChildren && entry.Kind != EntryKind.Folder))
        {
            var children = await hierarchyClient.ListEntriesAsync(
                new ListEntriesRequest { ProjectId = projectId, WatchId = watchId, FolderId = parent.Id },
                headers, cancellationToken: TestContext.Current.CancellationToken);
            foreach (var child in children.Entries.Entries_)
            {
                byName[child.Name] = child.Id;
            }
        }

        return byName;
    }

    /// <summary>The canvas's own selection shape: the <c>.adp</c> file, then the element as its child.</summary>
    private static ContextSelection ElementChain(Common.Wire.ShortGuid entryId, string elementId) =>
        new()
        {
            Source = ContextSelectionSource.Explorer,
            Id = new ContextSource { EntryId = entryId },
            Path = new Path(),
            Child = new ContextSelection
            {
                Source = ContextSelectionSource.DiagramCanvas,
                Id = new ContextSource { ElementId = new ElementId { Value = elementId } },
                Path = new Path(),
            },
        };

    private static Task<ExecuteActionResponse> ExecuteProjectActionAsync(
        ContextService.ContextServiceClient contextClient,
        Common.Wire.ShortGuid projectId,
        Common.Wire.ShortGuid watchId,
        Metadata headers,
        string actionId) =>
        contextClient.ExecuteActionAsync(
            new ExecuteActionRequest
            {
                ProjectId = projectId,
                WatchId = watchId,
                Source = new ContextSource { Project = new Google.Protobuf.WellKnownTypes.Empty() },
                InteractionId = ShortGuid.NewShortGuid(),
                ActionId = actionId,
            },
            headers,
            cancellationToken: TestContext.Current.CancellationToken).ResponseAsync;

    private static Path PathOf(string fileName)
    {
        var path = new Path();
        path.Segments.Add(fileName);
        return path;
    }

    private static CancellationTokenSource CreateMessageTimeout()
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cts.CancelAfter(MessageTimeout);
        return cts;
    }

    private GrpcChannel CreateChannel()
    {
        var httpClient = _factory.CreateDefaultClient();
        return GrpcChannel.ForAddress(httpClient.BaseAddress!, new GrpcChannelOptions { HttpClient = httpClient });
    }

    private static async Task<Metadata> LoginAsync(GrpcChannel channel)
    {
        var authClient = new AuthenticationService.AuthenticationServiceClient(channel);
        var response = await authClient.LoginAsync(
            new LoginRequest { Username = DeveloperUsername, Credential = DeveloperCredential },
            cancellationToken: TestContext.Current.CancellationToken);
        return new Metadata { { SessionTokenHeader, response.Session.Value } };
    }

    private async Task<ShortGuid> AddProjectAsync(GrpcChannel channel, Metadata headers)
    {
        var projectClient = new ProjectService.ProjectServiceClient(channel);
        var pathMessage = new Path();
        pathMessage.Segments.AddRange(_projectFolder.Split(IoPath.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries));
        var response = await projectClient.AddProjectAsync(
            new AddProjectRequest { Path = pathMessage },
            headers,
            cancellationToken: TestContext.Current.CancellationToken);
        return response.Added.Id;
    }
}
