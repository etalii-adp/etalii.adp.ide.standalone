using EtAlii.Adp.Authentication.Wire;
using EtAlii.Adp.Common;
using EtAlii.Adp.Common.Wire;
using EtAlii.Adp.Context.Wire;
using EtAlii.Adp.Diagram.Rdf;
using EtAlii.Adp.Diagram.Wire;
using EtAlii.Adp.Hierarchy;
using EtAlii.Adp.Hierarchy.Wire;
using EtAlii.Adp.History;
using EtAlii.Adp.Projects;
using EtAlii.Adp.Projects.Wire;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;
using HierarchyService = EtAlii.Adp.Hierarchy.Wire.HierarchyService;
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here
using Path = EtAlii.Adp.Common.Wire.Path;
using ProjectService = EtAlii.Adp.Projects.Wire.ProjectService;

namespace EtAlii.Adp.Backend.Tests;

/// <summary>
/// The RDF arc over the real host: a registered Turtle file streaming cards and edges, a bare
/// file routing on sight, a reposition landing in the <c>.adp</c>'s <c>layout:</c> block while
/// the RDF file never changes by a byte, a label edit travelling the whole selection chain, and
/// two registrations sharing one document (rdf-diagram Requirements 2 and 4, and the
/// integration halves of 1 and 5).
/// </summary>
public class RdfFlowTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private const string DeveloperUsername = "admin";
    private const string DeveloperCredential = "changeme";
    private const string SessionTokenHeader = "session-token";
    private static readonly TimeSpan MessageTimeout = TimeSpan.FromSeconds(20);

    private const string CurieId = "res:http://example.org/curie";

    private const string Curie = """
        @prefix ex: <http://example.org/> .
        @prefix rdfs: <http://www.w3.org/2000/01/rdf-schema#> .

        ex:curie a ex:Scientist ;
            rdfs:label "Marie Curie" ;
            ex:discovered ex:radium, ex:polonium .

        ex:radium rdfs:label "Radium" .
        """;

    private readonly WebApplicationFactory<Program> _factory;
    private readonly string _appDataRoot;
    private readonly string _projectFolder;

    public RdfFlowTests(WebApplicationFactory<Program> baseFactory)
    {
        _appDataRoot = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.IntegrationTests", Guid.NewGuid().ToString("N"));
        _projectFolder = IoPath.Combine(_appDataRoot, "sample-project");
        Directory.CreateDirectory(_projectFolder);

        // One body, two registrations - and one bare file with no registration at all.
        File.WriteAllText(IoPath.Combine(_projectFolder, "curie.ttl"), Curie);
        File.WriteAllText(IoPath.Combine(_projectFolder, "curie.adp"), "w3c/rdf\nbody: curie.ttl\n");
        File.WriteAllText(IoPath.Combine(_projectFolder, "curie.second.adp"), "w3c/rdf\nbody: curie.ttl\n");
        File.WriteAllText(IoPath.Combine(_projectFolder, "bare.ttl"), Curie);

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
    public async Task ARegisteredFile_StreamsCardsAndEdges_AndABareOne_RoutesOnSight()
    {
        // Arrange: Requirement 2.2 - a bare .ttl needs no ceremony, and a registered one opens
        // its reading like any arranged module's.
        using var channel = CreateChannel();
        var headers = await LoginAsync(channel);
        var projectId = await AddProjectAsync(channel, headers);

        // Act.
        var registered = await BaselineAsync(channel, headers, projectId, "curie.adp");
        var bare = await BaselineAsync(channel, headers, projectId, "bare.ttl");

        // Assert: cards for the IRI terms, the discovered edges, the literal folded away.
        Assert.Contains(registered, element => element.Id.Value == CurieId && element.Type == "w3c/rdf+resource");
        Assert.Contains(registered, element => element.Id.Value == "res:http://example.org/radium");
        Assert.Contains(registered, element =>
            element.Id.Value == $"edge:{CurieId}|http://example.org/discovered|res:http://example.org/radium");
        Assert.DoesNotContain(registered, element => element.Id.Value.Contains("Marie", StringComparison.Ordinal));

        Assert.Contains(bare, element => element.Id.Value == CurieId);
    }

    [Fact]
    public async Task AReposition_LandsInTheAdp_SurvivesReopen_AndUndoReturnsIt_WithTheBodyUntouchedThroughout()
    {
        // Arrange: the layout-persistence promise (Requirement 4), end to end. The diagram must
        // be open on the SAME connection, because core will not edit a document a caller is not
        // looking at.
        using var channel = CreateChannel();
        var client = new DiagramService.DiagramServiceClient(channel);
        var contextClient = new ContextService.ContextServiceClient(channel);
        var headers = await LoginAsync(channel);
        var projectId = await AddProjectAsync(channel, headers);
        var watchId = ShortGuid.NewShortGuid();
        var bodyPath = IoPath.Combine(_projectFolder, "curie.ttl");
        var adpPath = IoPath.Combine(_projectFolder, "curie.adp");
        var bodyBefore = await File.ReadAllBytesAsync(bodyPath, TestContext.Current.CancellationToken);
        var adpBefore = await File.ReadAllTextAsync(adpPath, TestContext.Current.CancellationToken);

        using var cts = CreateMessageTimeout();
        using var call = client.Open(
            new OpenDiagramRequest { ProjectId = projectId, WatchId = watchId, Path = PathOf("curie.adp") },
            headers, cancellationToken: cts.Token);
        await ReadAddAsync(call.ResponseStream, cts.Token);

        // Act: drag the card to an authored spot.
        var moved = await client.MoveElementAsync(
            new MoveElementRequest
            {
                ProjectId = projectId,
                WatchId = watchId,
                Path = PathOf("curie.adp"),
                ElementId = CurieId,
                Position = new Point2D { X = 300, Y = 180 },
            },
            headers, cancellationToken: TestContext.Current.CancellationToken);

        // Assert: the position is in the registration, and nowhere near the RDF (4.1).
        Assert.Equal("", moved.Error);
        Assert.Equal(new RegistrationPosition(300, 180), RegistrationLayout.Read(adpPath)[CurieId]);
        Assert.Equal(bodyBefore, await File.ReadAllBytesAsync(bodyPath, TestContext.Current.CancellationToken));

        // Act, continued: a fresh open sees the stored position overlaid (4.2).
        var reopened = await BaselineAsync(channel, headers, projectId, "curie.adp");
        var card = reopened.Single(element => element.Id.Value == CurieId);
        Assert.Equal((300d, 180d), (card.Position.X, card.Position.Y));

        // Act, continued: one undo returns the registration byte for byte (4.2).
        var undone = await ExecuteProjectActionAsync(contextClient, projectId, watchId, headers, HistoryContextActionProvider.UndoActionId);

        // Assert.
        Assert.True(undone.Accepted, undone.Error);
        Assert.Equal(adpBefore, await File.ReadAllTextAsync(adpPath, TestContext.Current.CancellationToken));
        Assert.Equal(bodyBefore, await File.ReadAllBytesAsync(bodyPath, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ALabelEdit_TravelsTheSelectionChain_RewritesOneToken_AndIsOneUndoAway()
    {
        // Arrange: the property path over the real selection chain - which also proves an RDF
        // element resolves while every other module's resolver is registered beside this one.
        using var channel = CreateChannel();
        var contextClient = new ContextService.ContextServiceClient(channel);
        var headers = await LoginAsync(channel);
        var projectId = await AddProjectAsync(channel, headers);
        var watchId = ShortGuid.NewShortGuid();
        var entries = await EntriesAsync(channel, headers, projectId, watchId);
        var bodyPath = IoPath.Combine(_projectFolder, "curie.ttl");
        var before = await File.ReadAllTextAsync(bodyPath, TestContext.Current.CancellationToken);

        var selected = await contextClient.SelectAsync(
            new SelectRequest
            {
                ProjectId = projectId,
                WatchId = watchId,
                Selection = ElementChain(entries["curie.adp"], CurieId),
            },
            headers, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Empty(selected.Error);

        // Act.
        var result = await contextClient.SetPropertyAsync(
            new SetPropertyRequest
            {
                ProjectId = projectId,
                WatchId = watchId,
                PropertyId = RdfContextPropertyProvider.LabelProperty,
                Value = "Curie, Marie",
            },
            headers, cancellationToken: TestContext.Current.CancellationToken);

        // Assert: exactly the literal token was rewritten, keeping the author's abbreviations.
        Assert.True(result.Accepted, result.Error);
        var edited = await File.ReadAllTextAsync(bodyPath, TestContext.Current.CancellationToken);
        Assert.Contains("rdfs:label \"Curie, Marie\" ;", edited, StringComparison.Ordinal);
        Assert.Contains("ex:discovered ex:radium, ex:polonium .", edited, StringComparison.Ordinal);

        // Act, continued: one undo returns the RDF byte for byte.
        var undone = await ExecuteProjectActionAsync(contextClient, projectId, watchId, headers, HistoryContextActionProvider.UndoActionId);

        // Assert.
        Assert.True(undone.Accepted, undone.Error);
        Assert.Equal(before, await File.ReadAllTextAsync(bodyPath, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task TwoRegistrationsOnOneBody_ShareTheEditedDocument()
    {
        // Arrange: Requirement 2.4 - one store, one document, one history; an edit through one
        // registration is visible through the other.
        using var channel = CreateChannel();
        var contextClient = new ContextService.ContextServiceClient(channel);
        var headers = await LoginAsync(channel);
        var projectId = await AddProjectAsync(channel, headers);
        var watchId = ShortGuid.NewShortGuid();
        var entries = await EntriesAsync(channel, headers, projectId, watchId);

        var selected = await contextClient.SelectAsync(
            new SelectRequest
            {
                ProjectId = projectId,
                WatchId = watchId,
                Selection = ElementChain(entries["curie.adp"], "res:http://example.org/radium"),
            },
            headers, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Empty(selected.Error);

        // Act: edit through the first registration…
        var result = await contextClient.SetPropertyAsync(
            new SetPropertyRequest
            {
                ProjectId = projectId,
                WatchId = watchId,
                PropertyId = RdfContextPropertyProvider.LabelProperty,
                Value = "Radium (Ra)",
            },
            headers, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(result.Accepted, result.Error);

        // …and open the second: the same document, already carrying the edit.
        var second = await BaselineAsync(channel, headers, projectId, "curie.second.adp");

        // Assert.
        Assert.Contains(second, element => element.Id.Value == "res:http://example.org/radium");
        var payload = second.Single(element => element.Id.Value == "res:http://example.org/radium");
        var decoded = RdfResourcePayload.Parser.ParseFrom(payload.Payload.Value);
        Assert.Contains(decoded.Rows, row => row.Value == "Radium (Ra)");
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
