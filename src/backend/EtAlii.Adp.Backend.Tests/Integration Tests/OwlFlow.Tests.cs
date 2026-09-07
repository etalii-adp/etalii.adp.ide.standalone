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
/// The ontology arc over the real host: a registered ontology streaming its VOWL elements, a
/// class reposition landing in the <c>.adp</c> while the ontology file never changes by a byte,
/// an expression node refusing that same reposition with the identity boundary's sentence, and
/// one file open under both family readings sharing one document and one history (owl-diagram
/// Requirements 3.2, 4.1, 8.1-8.4).
/// </summary>
public class OwlFlowTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private const string DeveloperUsername = "admin";
    private const string DeveloperCredential = "changeme";
    private const string SessionTokenHeader = "session-token";
    private static readonly TimeSpan MessageTimeout = TimeSpan.FromSeconds(20);

    private const string Ns = "http://example.org/pizza#";
    private const string PizzaId = $"res:{Ns}Pizza";
    private const string ExpressionId = $"expr:{Ns}Vegetarian|http://www.w3.org/2000/01/rdf-schema#subClassOf|1";

    private const string Ontology = """
        @prefix : <http://example.org/pizza#> .
        @prefix owl: <http://www.w3.org/2002/07/owl#> .
        @prefix rdfs: <http://www.w3.org/2000/01/rdf-schema#> .

        <http://example.org/pizza> a owl:Ontology ;
            rdfs:label "Pizzas" .

        :Pizza a owl:Class ;
            rdfs:label "Pizza" .

        :Topping a owl:Class .

        :Vegetarian a owl:Class ;
            rdfs:subClassOf :Pizza ;
            rdfs:subClassOf [ a owl:Restriction ; owl:onProperty :hasTopping ; owl:allValuesFrom :Topping ] .

        :hasTopping a owl:ObjectProperty ;
            rdfs:domain :Pizza ;
            rdfs:range :Topping .
        """;

    private readonly WebApplicationFactory<Program> _factory;
    private readonly string _appDataRoot;
    private readonly string _projectFolder;

    public OwlFlowTests(WebApplicationFactory<Program> baseFactory)
    {
        _appDataRoot = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.IntegrationTests", Guid.NewGuid().ToString("N"));
        _projectFolder = IoPath.Combine(_appDataRoot, "sample-project");
        Directory.CreateDirectory(_projectFolder);

        // One body, two readings of it - the ontology reading and the family's graph reading.
        File.WriteAllText(IoPath.Combine(_projectFolder, "pizza.ttl"), Ontology);
        File.WriteAllText(IoPath.Combine(_projectFolder, "pizza.adp"), "w3c/owl\nbody: pizza.ttl\n");
        File.WriteAllText(IoPath.Combine(_projectFolder, "pizza.graph.adp"), "w3c/rdf\nbody: pizza.ttl\n");

        // A body with no registration beside it, to prove what a BARE file of this family opens
        // as - marker triple and all.
        File.WriteAllText(IoPath.Combine(_projectFolder, "bare.ttl"), Ontology);

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
    public async Task ARegisteredOntology_StreamsItsShapesAndAxioms_AndABareBodyStaysTheGraphReadings()
    {
        // Arrange.
        using var channel = CreateChannel();
        var headers = await LoginAsync(channel);
        var projectId = await AddProjectAsync(channel, headers);

        // Act.
        var ontology = await BaselineAsync(channel, headers, projectId, "pizza.adp");
        var bare = await BaselineAsync(channel, headers, projectId, "bare.ttl");

        // Assert: the ontology's own element types, the expression node among them (R1, R3.1).
        Assert.Contains(ontology, element => element.Id.Value == PizzaId && element.Type == OwlElementMapper.NodeType);
        Assert.Contains(ontology, element => element.Type == OwlElementMapper.ExpressionType);
        Assert.Contains(ontology, element => element.Type == OwlElementMapper.EdgeType);

        // A bare body is the anchor's, never a reading's, even when it carries the owl:Ontology
        // marker - the routing arrangement (R8.2). It opens as the data graph, whose resource
        // cards are a different element type entirely. (pizza.ttl is not bare: a registration
        // sits beside it, and the registration wins.)
        Assert.Contains(bare, element => element.Type == RdfElementMapper.ResourceType);
        Assert.DoesNotContain(bare, element => element.Type == OwlElementMapper.NodeType);
    }

    [Fact]
    public async Task AClassReposition_LandsInTheAdp_SurvivesReopen_AndTheOntologyNeverChanges()
    {
        // Arrange.
        using var channel = CreateChannel();
        var client = new DiagramService.DiagramServiceClient(channel);
        var contextClient = new ContextService.ContextServiceClient(channel);
        var headers = await LoginAsync(channel);
        var projectId = await AddProjectAsync(channel, headers);
        var watchId = ShortGuid.NewShortGuid();
        var bodyPath = IoPath.Combine(_projectFolder, "pizza.ttl");
        var adpPath = IoPath.Combine(_projectFolder, "pizza.adp");
        var bodyBefore = await File.ReadAllBytesAsync(bodyPath, TestContext.Current.CancellationToken);
        var adpBefore = await File.ReadAllTextAsync(adpPath, TestContext.Current.CancellationToken);

        using var cts = CreateMessageTimeout();
        using var call = client.Open(
            new OpenDiagramRequest { ProjectId = projectId, WatchId = watchId, Path = PathOf("pizza.adp") },
            headers, cancellationToken: cts.Token);
        await ReadAddAsync(call.ResponseStream, cts.Token);

        // Act.
        var moved = await client.MoveElementAsync(
            new MoveElementRequest
            {
                ProjectId = projectId,
                WatchId = watchId,
                Path = PathOf("pizza.adp"),
                ElementId = PizzaId,
                Position = new Point2D { X = 420, Y = 260 },
            },
            headers, cancellationToken: TestContext.Current.CancellationToken);

        // Assert: the position is in the registration, and nowhere near the ontology (R4.1).
        Assert.Equal("", moved.Error);
        Assert.Equal(new RegistrationPosition(420, 260), RegistrationLayout.Read(adpPath)[PizzaId]);
        Assert.Equal(bodyBefore, await File.ReadAllBytesAsync(bodyPath, TestContext.Current.CancellationToken));

        // Act, continued: a fresh open sees the stored position overlaid.
        var reopened = await BaselineAsync(channel, headers, projectId, "pizza.adp");
        var drawn = reopened.Single(element => element.Id.Value == PizzaId);
        Assert.Equal((420d, 260d), (drawn.Position.X, drawn.Position.Y));

        // Act, continued: one undo returns the registration byte for byte.
        var undone = await ExecuteProjectActionAsync(contextClient, projectId, watchId, headers, HistoryContextActionProvider.UndoActionId);

        // Assert.
        Assert.True(undone.Accepted, undone.Error);
        Assert.Equal(adpBefore, await File.ReadAllTextAsync(adpPath, TestContext.Current.CancellationToken));
        Assert.Equal(bodyBefore, await File.ReadAllBytesAsync(bodyPath, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AnExpressionNode_RefusesTheReposition_WithTheBoundarysSentence()
    {
        // Arrange: the identity boundary, end to end - the refusal a user actually reads (R3.2).
        using var channel = CreateChannel();
        var client = new DiagramService.DiagramServiceClient(channel);
        var headers = await LoginAsync(channel);
        var projectId = await AddProjectAsync(channel, headers);
        var watchId = ShortGuid.NewShortGuid();
        var adpPath = IoPath.Combine(_projectFolder, "pizza.adp");

        using var cts = CreateMessageTimeout();
        using var call = client.Open(
            new OpenDiagramRequest { ProjectId = projectId, WatchId = watchId, Path = PathOf("pizza.adp") },
            headers, cancellationToken: cts.Token);
        var elements = await ReadAddAsync(call.ResponseStream, cts.Token);
        Assert.Contains(elements, element => element.Id.Value == ExpressionId);

        // Act.
        var moved = await client.MoveElementAsync(
            new MoveElementRequest
            {
                ProjectId = projectId,
                WatchId = watchId,
                Path = PathOf("pizza.adp"),
                ElementId = ExpressionId,
                Position = new Point2D { X = 10, Y = 20 },
            },
            headers, cancellationToken: TestContext.Current.CancellationToken);

        // Assert: refused with the reason, and nothing stored for it.
        Assert.Contains("does not survive an edit", moved.Error, StringComparison.Ordinal);
        Assert.DoesNotContain(ExpressionId, RegistrationLayout.Read(adpPath).Keys);
    }

    [Fact]
    public async Task TwoReadingsOfOneOntology_ShareTheEditedDocument()
    {
        // Arrange: one body under w3c/owl and w3c/rdf at once (R8.4).
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
                Selection = ElementChain(entries["pizza.adp"], PizzaId),
            },
            headers, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Empty(selected.Error);

        // Act: a label edit through the ontology reading, riding the family's writer…
        var result = await contextClient.SetPropertyAsync(
            new SetPropertyRequest
            {
                ProjectId = projectId,
                WatchId = watchId,
                PropertyId = RdfContextPropertyProvider.LabelProperty,
                Value = "Pizza (round)",
            },
            headers, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(result.Accepted, result.Error);

        // …and the graph reading of the same body already carries it.
        var graph = await BaselineAsync(channel, headers, projectId, "pizza.graph.adp");
        var card = graph.Single(element => element.Id.Value == PizzaId && element.Type == RdfElementMapper.ResourceType);
        var decoded = RdfResourcePayload.Parser.ParseFrom(card.Payload.Value);
        Assert.Contains(decoded.Rows, row => row.Value == "Pizza (round)");
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

    /// <summary>Every entry of the project, by name - one nested level deep, because a registration is a child of the subject it names.</summary>
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
