using EtAlii.Adp.Context;
using EtAlii.Adp.Context.Wire;
using EtAlii.Adp.Diagram.Rdf;
using EtAlii.Adp.Diagram.Wire;
using EtAlii.Adp.Documents.Wire;
using EtAlii.Adp.Hierarchy;
using EtAlii.Adp.TestSupport.Host;
using Xunit;
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Backend.Tests;

/// <summary>
/// The ontology arc over the real host: a registered ontology streaming its VOWL elements, a
/// class reposition landing in the <c>.adp</c> while the ontology file never changes by a byte,
/// an expression node refusing that same reposition with the identity boundary's sentence, and
/// one file open under both family readings sharing one document and one history (owl-diagram
/// Requirements 3.2, 4.1, 8.1-8.4).
/// </summary>
public class OwlFlowTests(HostFixture host) : IClassFixture<HostFixture>
{
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

    private readonly string _projectFolder = Seed(host.NewProjectFolder());

    private static string Seed(string projectFolder)
    {
        // One body, two readings of it - the ontology reading and the family's graph reading.
        File.WriteAllText(IoPath.Combine(projectFolder, "pizza.ttl"), Ontology);
        File.WriteAllText(IoPath.Combine(projectFolder, "pizza.adp"), "w3c/owl\nbody: pizza.ttl\n");
        File.WriteAllText(IoPath.Combine(projectFolder, "pizza.graph.adp"), "w3c/rdf\nbody: pizza.ttl\n");

        // A body with no registration beside it, to prove what a BARE file of this family opens
        // as - marker triple and all.
        File.WriteAllText(IoPath.Combine(projectFolder, "bare.ttl"), Ontology);
        return projectFolder;
    }

    [Fact]
    public async Task ARegisteredOntology_StreamsItsShapesAndAxioms_AndABareBodyStaysTheGraphReadings()
    {
        // Arrange.
        using var session = await FlowSession.OpenAsync(host, _projectFolder, TestContext.Current.CancellationToken);

        // Act.
        var ontology = await DiagramFlow.BaselineAsync(session, "pizza.adp");
        var bare = await DiagramFlow.BaselineAsync(session, "bare.ttl");

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
        using var session = await FlowSession.OpenAsync(host, _projectFolder, TestContext.Current.CancellationToken);
        var client = new DiagramService.DiagramServiceClient(session.Channel);
        var contextClient = session.Context;
        var watchId = ShortGuid.NewShortGuid();
        var bodyPath = IoPath.Combine(_projectFolder, "pizza.ttl");
        var adpPath = IoPath.Combine(_projectFolder, "pizza.adp");
        var bodyBefore = await File.ReadAllBytesAsync(bodyPath, TestContext.Current.CancellationToken);
        var adpBefore = await File.ReadAllTextAsync(adpPath, TestContext.Current.CancellationToken);

        using var cts = FlowSession.CreateMessageTimeout(TestContext.Current.CancellationToken);
        using var call = client.Open(
            new OpenDiagramRequest { ProjectId = session.ProjectId, WatchId = watchId, Path = DiagramFlow.PathOf("pizza.adp") },
            session.Headers, cancellationToken: cts.Token);
        await DiagramFlow.ReadAddAsync(call.ResponseStream, cts.Token);

        // Act.
        var moved = await client.MoveElementAsync(
            new MoveElementRequest
            {
                ProjectId = session.ProjectId,
                WatchId = watchId,
                Path = DiagramFlow.PathOf("pizza.adp"),
                ElementId = PizzaId,
                Position = new Point2D { X = 420, Y = 260 },
            },
            session.Headers, cancellationToken: TestContext.Current.CancellationToken);

        // Assert: the position is in the registration, and nowhere near the ontology (R4.1).
        Assert.Equal("", moved.Error);
        Assert.Equal(new RegistrationPosition(420, 260), RegistrationLayout.Read(adpPath)[PizzaId]);
        Assert.Equal(bodyBefore, await File.ReadAllBytesAsync(bodyPath, TestContext.Current.CancellationToken));

        // Act, continued: a fresh open sees the stored position overlaid.
        var reopened = await DiagramFlow.BaselineAsync(session, "pizza.adp");
        var drawn = reopened.Single(element => element.Id.Value == PizzaId);
        Assert.Equal((420d, 260d), (drawn.Position.X, drawn.Position.Y));

        // Act, continued: one undo returns the registration byte for byte.
        var undone = await DiagramFlow.ExecuteProjectActionAsync(session, watchId, HistoryContextActionProvider.UndoActionId);

        // Assert.
        Assert.True(undone.Accepted, undone.Error);
        Assert.Equal(adpBefore, await File.ReadAllTextAsync(adpPath, TestContext.Current.CancellationToken));
        Assert.Equal(bodyBefore, await File.ReadAllBytesAsync(bodyPath, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AnExpressionNode_RefusesTheReposition_WithTheBoundarysSentence()
    {
        // Arrange: the identity boundary, end to end - the refusal a user actually reads (R3.2).
        using var session = await FlowSession.OpenAsync(host, _projectFolder, TestContext.Current.CancellationToken);
        var client = new DiagramService.DiagramServiceClient(session.Channel);
        var watchId = ShortGuid.NewShortGuid();
        var adpPath = IoPath.Combine(_projectFolder, "pizza.adp");

        using var cts = FlowSession.CreateMessageTimeout(TestContext.Current.CancellationToken);
        using var call = client.Open(
            new OpenDiagramRequest { ProjectId = session.ProjectId, WatchId = watchId, Path = DiagramFlow.PathOf("pizza.adp") },
            session.Headers, cancellationToken: cts.Token);
        var elements = await DiagramFlow.ReadAddAsync(call.ResponseStream, cts.Token);
        Assert.Contains(elements, element => element.Id.Value == ExpressionId);

        // Act.
        var moved = await client.MoveElementAsync(
            new MoveElementRequest
            {
                ProjectId = session.ProjectId,
                WatchId = watchId,
                Path = DiagramFlow.PathOf("pizza.adp"),
                ElementId = ExpressionId,
                Position = new Point2D { X = 10, Y = 20 },
            },
            session.Headers, cancellationToken: TestContext.Current.CancellationToken);

        // Assert: refused with the reason, and nothing stored for it.
        Assert.Contains("does not survive an edit", moved.Error, StringComparison.Ordinal);
        Assert.DoesNotContain(ExpressionId, RegistrationLayout.Read(adpPath).Keys);
    }

    [Fact]
    public async Task TwoReadingsOfOneOntology_ShareTheEditedDocument()
    {
        // Arrange: one body under w3c/owl and w3c/rdf at once (R8.4).
        using var session = await FlowSession.OpenAsync(host, _projectFolder, TestContext.Current.CancellationToken);
        var contextClient = session.Context;
        var watchId = ShortGuid.NewShortGuid();
        var entries = await DiagramFlow.EntriesAsync(session, watchId);

        var selected = await contextClient.SelectAsync(
            new SelectRequest
            {
                ProjectId = session.ProjectId,
                WatchId = watchId,
                Selection = DiagramFlow.ElementChain(entries["pizza.adp"], PizzaId),
            },
            session.Headers, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Empty(selected.Error);

        // Act: a label edit through the ontology reading, riding the family's writer…
        var result = await contextClient.SetPropertyAsync(
            new SetPropertyRequest
            {
                ProjectId = session.ProjectId,
                WatchId = watchId,
                PropertyId = RdfContextPropertyProvider.LabelProperty,
                Value = "Pizza (round)",
            },
            session.Headers, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(result.Accepted, result.Error);

        // …and the graph reading of the same body already carries it.
        var graph = await DiagramFlow.BaselineAsync(session, "pizza.graph.adp");
        var card = graph.Single(element => element.Id.Value == PizzaId && element.Type == RdfElementMapper.ResourceType);
        var decoded = RdfResourcePayload.Parser.ParseFrom(card.Payload.Value);
        Assert.Contains(decoded.Rows, row => row.Value == "Pizza (round)");
    }
}
