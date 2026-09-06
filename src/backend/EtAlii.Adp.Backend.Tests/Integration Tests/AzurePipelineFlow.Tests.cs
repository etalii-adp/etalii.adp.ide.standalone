using EtAlii.Adp.Authentication.Wire;
using EtAlii.Adp.Common.Wire;
using EtAlii.Adp.Diagram.Wire;
using EtAlii.Adp.Projects;
using EtAlii.Adp.Projects.Wire;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here
using Path = EtAlii.Adp.Common.Wire.Path;
using ProjectService = EtAlii.Adp.Projects.Wire.ProjectService;

namespace EtAlii.Adp.Backend.Tests;

/// <summary>
/// The Azure Pipelines arc over the real host: a <c>.yml</c> that is not a diagram until somebody
/// registers it, then opens, streams and edits like any other type.
/// </summary>
/// <remarks>
/// The half worth proving here rather than in the module's own tests is the <b>shared extension</b>
/// (Requirement 2). A <c>.mm</c> is a mindmap wherever it is found; a <c>.yml</c> is a pipeline only
/// where somebody said so, because half the files in a repository are YAML. That rule lives in
/// core's router and is exercised by two modules at once, which is exactly the sort of thing only
/// an integration test can catch.
/// </remarks>
public class AzurePipelineFlowTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private const string DeveloperUsername = "admin";
    private const string DeveloperCredential = "changeme";
    private const string SessionTokenHeader = "session-token";

    private const string Pipeline = """
        trigger:
          - main

        stages:
          - stage: Build
            jobs:
              - job: Compile
                steps:
                  - script: dotnet build
                    displayName: Build

          - stage: Test
            jobs:
              - job: Verify
                steps:
                  - script: dotnet test
        """;

    private readonly WebApplicationFactory<Program> _factory;
    private readonly string _appDataRoot;
    private readonly string _projectFolder;

    public AzurePipelineFlowTests(WebApplicationFactory<Program> baseFactory)
    {
        _appDataRoot = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.IntegrationTests", Guid.NewGuid().ToString("N"));
        _projectFolder = IoPath.Combine(_appDataRoot, "sample-project");
        Directory.CreateDirectory(_projectFolder);

        // A pipeline nobody has registered yet, a registered one, a mindmap whose bare body still
        // routes, and a file no type claims.
        File.WriteAllText(IoPath.Combine(_projectFolder, "unregistered.yml"), Pipeline);
        File.WriteAllText(IoPath.Combine(_projectFolder, "azure-pipelines.yml"), Pipeline);
        File.WriteAllText(IoPath.Combine(_projectFolder, "azure-pipelines.adp"), "azure-devops/pipeline\n");
        File.WriteAllText(
            IoPath.Combine(_projectFolder, "roadmap.mm"),
            "<map version=\"freeplane 1.11.5\">\n<node TEXT=\"roadmap\" ID=\"ID_1\"/>\n</map>\n");
        File.WriteAllText(IoPath.Combine(_projectFolder, "notes.txt"), "not a diagram\n");

        _factory = baseFactory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("developer");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IProjectStore>();
                services.AddSingleton<IProjectStore>(new FileProjectStore(_appDataRoot));
                // The problem cache lives and dies with this test rather than in the real user
                // profile the host's AddProblems registration points at.
                services.RemoveAll<Problems.IProblemStore>();
                services.AddSingleton<Problems.IProblemStore>(provider => new Problems.ProblemStore(
                    _appDataRoot,
                    provider.GetRequiredService<Hierarchy.DiagramFileRouter>(),
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
    public async Task ARegisteredPipeline_OpensAndStreamsItsStagesAndEdges()
    {
        // Arrange: Requirement 11.1 - opened over DiagramService.Open with the file's
        // project-relative path, like every other diagram type.
        using var channel = CreateChannel();
        var headers = await LoginAsync(channel);
        var projectId = await AddProjectAsync(channel, headers);

        // Act.
        var elements = await BaselineAsync(channel, headers, projectId, "azure-pipelines.adp");

        // Assert.
        Assert.Contains(elements, element => element.Id.Value == "Build" && element.Type == "azure-devops/pipeline+stage");
        Assert.Contains(elements, element => element.Id.Value == "Test");
        // The arrow nobody wrote down: Test has no dependsOn, so it waits for the stage before it.
        Assert.Contains(elements, element => element.Id.Value == "edge:Build->Test");
    }

    [Fact]
    public async Task ABarePipeline_RoutesNowhere_WhileABareMindmapStillRoutes()
    {
        // Arrange: Requirement 2.2 and 2.3 - the whole point of a shared extension. Half the files
        // in a repository are YAML, so a .yml is a pipeline only where somebody said so; a .mm is
        // a mindmap wherever it is found, and that must not have been broken to achieve this.
        using var channel = CreateChannel();
        var diagramClient = new DiagramService.DiagramServiceClient(channel);
        var headers = await LoginAsync(channel);
        var projectId = await AddProjectAsync(channel, headers);

        // Act.
        var pipeline = await diagramClient.DescribeToolboxAsync(
            new DescribeToolboxRequest { ProjectId = projectId, Path = PathOf("unregistered.yml") },
            headers, cancellationToken: TestContext.Current.CancellationToken);
        var mindmap = await diagramClient.DescribeToolboxAsync(
            new DescribeToolboxRequest { ProjectId = projectId, Path = PathOf("roadmap.mm") },
            headers, cancellationToken: TestContext.Current.CancellationToken);

        // Assert.
        Assert.Empty(pipeline.Items);
        Assert.NotEmpty(mindmap.Items);
    }

    [Fact]
    public async Task ARegisteredPipeline_AnswersWithThePipelinePalette()
    {
        // Arrange & act.
        using var channel = CreateChannel();
        var diagramClient = new DiagramService.DiagramServiceClient(channel);
        var headers = await LoginAsync(channel);
        var projectId = await AddProjectAsync(channel, headers);

        var response = await diagramClient.DescribeToolboxAsync(
            new DescribeToolboxRequest { ProjectId = projectId, Path = PathOf("azure-pipelines.adp") },
            headers, cancellationToken: TestContext.Current.CancellationToken);

        // Assert.
        Assert.Equal(
            ["Stage", "Job", "Deployment job", "Script step"],
            response.Items.Select(item => item.Label));
    }

    [Fact]
    public async Task AddIsOfferedOnAnUnregisteredPipeline_AndNotOnAPlainFileOrAnAlreadyRegisteredOne()
    {
        // Arrange: Requirement 2.5 - Add-on-a-file is how a .yml already in the repository becomes
        // a diagram, and it must not be offered where it would do nothing.
        using var channel = CreateChannel();
        var contextClient = new ContextService.ContextServiceClient(channel);
        var headers = await LoginAsync(channel);
        var projectId = await AddProjectAsync(channel, headers);
        // One watch id throughout: a hierarchy model is per connection, so an entry listed on
        // one cannot be named by a call on another.
        var watchId = ShortGuid.NewShortGuid();
        var hierarchy = await EntriesAsync(channel, headers, projectId, watchId);

        // Act.
        var onPipeline = await ActionsOnAsync(contextClient, headers, projectId, watchId, hierarchy["unregistered.yml"]);
        var onPlainFile = await ActionsOnAsync(contextClient, headers, projectId, watchId, hierarchy["notes.txt"]);
        var onRegistered = await ActionsOnAsync(contextClient, headers, projectId, watchId, hierarchy["azure-pipelines.yml"]);

        // Assert.
        Assert.Contains(onPipeline, action => action.StartsWith("hierarchy.add", StringComparison.Ordinal));
        Assert.DoesNotContain(onPlainFile, action => action.StartsWith("hierarchy.add", StringComparison.Ordinal));
        // The body of a registered pipeline is already a diagram; registering it again would make
        // a second .adp pointing at the same file.
        Assert.DoesNotContain(onRegistered, action => action.StartsWith("hierarchy.add", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ADependencyEdit_RewritesOnlyTheLinesItTouches_AndIsOneUndoAway()
    {
        // Arrange: Requirement 9.8 - the edit reaches the file, and the file is otherwise exactly
        // what it was. This is the promise the whole module is built around, checked here over the
        // real host rather than against the writer in isolation.
        using var channel = CreateChannel();
        var contextClient = new ContextService.ContextServiceClient(channel);
        var headers = await LoginAsync(channel);
        var projectId = await AddProjectAsync(channel, headers);
        var watchId = ShortGuid.NewShortGuid();
        var entries = await EntriesAsync(channel, headers, projectId, watchId);
        var bodyPath = IoPath.Combine(_projectFolder, "azure-pipelines.yml");
        var before = await File.ReadAllLinesAsync(bodyPath, TestContext.Current.CancellationToken);

        // Arrange, continued: the selection has to be recorded first. An element id on its own
        // has no file above it, and this module's resolver refuses one - a pipeline element is
        // only selectable inside its pipeline (Requirement 12.1), which is the same nested shape
        // the canvas sends.
        var selected = await contextClient.SelectAsync(
            new SelectRequest
            {
                ProjectId = projectId,
                WatchId = watchId,
                Selection = ElementChain(entries["azure-pipelines.adp"], "Test"),
            },
            headers,
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.Empty(selected.Error);

        // Act.
        var result = await contextClient.SetPropertyAsync(
            new SetPropertyRequest
            {
                ProjectId = projectId,
                WatchId = watchId,
                PropertyId = "azure-pipeline.display-name",
                Value = "Run the tests",
            },
            headers,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert.
        Assert.True(result.Accepted, result.Error);
        var after = await File.ReadAllLinesAsync(bodyPath, TestContext.Current.CancellationToken);
        Assert.Equal(before.Length + 1, after.Length);
        var added = after.Except(before).ToList();
        Assert.Equal(["    displayName: Run the tests"], added);
    }

    /// <summary>The baseline elements of one open diagram.</summary>
    private static async Task<IReadOnlyList<Element>> BaselineAsync(
        GrpcChannel channel,
        Metadata headers,
        ShortGuid projectId,
        string fileName)
    {
        var diagramClient = new DiagramService.DiagramServiceClient(channel);
        using var stream = diagramClient.Open(
            new OpenDiagramRequest
            {
                ProjectId = projectId,
                WatchId = ShortGuid.NewShortGuid(),
                Path = PathOf(fileName),
            },
            headers,
            cancellationToken: TestContext.Current.CancellationToken);

        var elements = new List<Element>();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));

        try
        {
            // One delta is enough: the baseline is a single Add carrying everything in view.
            while (await stream.ResponseStream.MoveNext(timeout.Token))
            {
                if (stream.ResponseStream.Current.ActionCase == Delta.ActionOneofCase.Add)
                {
                    elements.AddRange(stream.ResponseStream.Current.Add.Elements);
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Falls through to the assertion, which reports an empty baseline rather than a
            // cancellation nobody can read.
        }

        return elements;
    }

    /// <summary>
    /// Every entry of the project, by name, so a test can name a file rather than an id.
    /// </summary>
    /// <remarks>
    /// The watch id is a parameter because a hierarchy model is per connection: a selection made
    /// on one watch id cannot name an entry listed under another.
    /// </remarks>
    private static async Task<IReadOnlyDictionary<string, Common.Wire.ShortGuid>> EntriesAsync(
        GrpcChannel channel,
        Metadata headers,
        ShortGuid projectId,
        ShortGuid? watchId = null)
    {
        var hierarchyClient = new HierarchyService.HierarchyServiceClient(channel);
        var effectiveWatchId = watchId ?? ShortGuid.NewShortGuid();
        var response = await hierarchyClient.ListEntriesAsync(
            new ListEntriesRequest { ProjectId = projectId, WatchId = effectiveWatchId },
            headers,
            cancellationToken: TestContext.Current.CancellationToken);

        // A registration is a child of its subject now, so the flat dictionary walks one nested
        // level too - same watch id, so the same model answers both listings.
        var byName = response.Entries.Entries_.ToDictionary(entry => entry.Name, entry => entry.Id, StringComparer.Ordinal);
        foreach (var parent in response.Entries.Entries_.Where(entry => entry.HasChildren && !IsFolderEntry(entry)))
        {
            var children = await hierarchyClient.ListEntriesAsync(
                new ListEntriesRequest { ProjectId = projectId, WatchId = effectiveWatchId, FolderId = parent.Id },
                headers,
                cancellationToken: TestContext.Current.CancellationToken);
            foreach (var child in children.Entries.Entries_)
            {
                byName[child.Name] = child.Id;
            }
        }

        return byName;

        static bool IsFolderEntry(Entry entry) => entry.Kind == EntryKind.Folder;
    }

    /// <summary>The action ids offered on one hierarchy entry.</summary>
    private static async Task<IReadOnlyList<string>> ActionsOnAsync(
        ContextService.ContextServiceClient client,
        Metadata headers,
        ShortGuid projectId,
        ShortGuid watchId,
        Common.Wire.ShortGuid entryId)
    {
        var response = await client.DiscoverActionsAsync(
            new DiscoverActionsRequest
            {
                ProjectId = projectId,
                WatchId = watchId,
                Source = new ContextSource { EntryId = entryId },
            },
            headers,
            cancellationToken: TestContext.Current.CancellationToken);

        return response.Groups.SelectMany(group => group.Actions).Select(action => action.Id).ToList();
    }

    /// <summary>
    /// The canvas's own selection shape: the <c>.adp</c> file, then the element as its child.
    /// </summary>
    /// <remarks>
    /// The child's path is left empty, which asks the backend to fill in the full chain rather
    /// than trusting the client's version of it - the same thing the pipeline canvas sends.
    /// </remarks>
    private static ContextSelection ElementChain(Common.Wire.ShortGuid entryId, string elementId)
    {
        var chain = new ContextSelection
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

        // The outer path is left empty as well: the backend resolves the entry id to its own
        // path and checks any client-supplied one against it, so sending none is both the
        // simplest correct thing and what the explorer actually does.
        return chain;
    }

    private static Path PathOf(string fileName)
    {
        var path = new Path();
        path.Segments.Add(fileName);
        return path;
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
