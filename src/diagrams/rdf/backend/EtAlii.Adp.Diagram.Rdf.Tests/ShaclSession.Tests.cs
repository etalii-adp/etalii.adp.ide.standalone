using EtAlii.Adp.Backend;
using EtAlii.Adp.Backend.Diagrams;
using EtAlii.Adp.Diagram.Rdf.Shacl;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.Rdf.Tests;

/// <summary>
/// The shapes reading over the family store (shacl-diagram Requirement 2): the definition's
/// shared-extension stance and its marker suggestion, one store serving two readings of one
/// file, and the session's refusals naming their reasons.
/// </summary>
public class ShaclSessionTests : IDisposable
{
    private readonly string _root;
    private readonly ServiceProvider _provider;

    public ShaclSessionTests()
    {
        _root = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _provider = new ServiceCollection()
            .AddSingleton<IReadOnlyList<DiagramDefinition>>(Diagram.Definitions)
            .AddCommands()
            .AddRdf()
            .BuildServiceProvider();
    }

    public void Dispose()
    {
        _provider.Dispose();
        TestFolder.TryDelete(_root);
        GC.SuppressFinalize(this);
    }

    private const string Shapes = """
        @prefix sh: <http://www.w3.org/ns/shacl#> .
        @prefix ex: <http://example.org/> .
        @prefix xsd: <http://www.w3.org/2001/XMLSchema#> .

        ex:PersonShape a sh:NodeShape ;
            sh:targetClass ex:Person ;
            sh:property [ sh:path ex:name ; sh:datatype xsd:string ; sh:minCount 1 ] .
        """;

    private string WriteBody(string name = "shapes.ttl", string text = Shapes)
    {
        var path = IoPath.Combine(_root, name);
        File.WriteAllText(path, text);
        return path;
    }

    private IDiagramSession Open(string bodyPath, string? registrationPath)
    {
        var factory = _provider.GetServices<IDiagramSessionFactory>()
            .Single(candidate => candidate.Origin == ServiceCollectionAddShaclExtension.ShaclOrigin);
        return factory.Open(ShortGuid.NewShortGuid(), _root, bodyPath, registrationPath);
    }

    [Fact]
    public void TheDefinition_NeverClaimsABareBody_AndDeclaresTheFamilyExtensions()
    {
        // The routing arrangement: SharedExtension keeps a bare .ttl the anchor's, while Add
        // offers this reading for both serializations.
        Assert.Contains(Diagram.Shacl, Diagram.Definitions);
        Assert.True(Diagram.Shacl.SharedExtension);
        Assert.True(Diagram.Shacl.DeclaresExtension(".ttl"));
        Assert.True(Diagram.Shacl.DeclaresExtension(".nt"));
        Assert.False(Diagram.Rdf.SharedExtension);
        Assert.Equal(new DiagramOrigin("w3c", "shacl"), Diagram.Shacl.Origin);
    }

    [Fact]
    public void TheDefinition_IsSuggestedOnMarkerTriples_AndNotOnAPlainGraph()
    {
        Assert.NotNull(Diagram.Shacl.SuggestsBody);

        // The markers the recommendation's own examples lead with.
        Assert.True(Diagram.Shacl.SuggestsBody!("ex:S a sh:NodeShape ."));
        Assert.True(Diagram.Shacl.SuggestsBody!("ex:S sh:property [ sh:path ex:p ] ."));
        Assert.True(Diagram.Shacl.SuggestsBody!("<x> a <http://www.w3.org/ns/shacl#NodeShape> ."));

        // A data graph is the anchor's business, and an ontology is the OWL reading's.
        Assert.False(Diagram.Shacl.SuggestsBody!("ex:alice a ex:Person ; ex:name \"Alice\" ."));
        Assert.False(Diagram.Shacl.SuggestsBody!("ex:o a owl:Ontology ."));
    }

    [Fact]
    public async Task TheSession_RendersTheShapes_WithChipsAndRowsInsideTheCard()
    {
        var body = WriteBody();

        await using var session = Open(body, null);
        var deltas = session.Baseline();

        var added = Assert.IsType<DiagramAddDelta>(Assert.Single(deltas));
        var card = Assert.Single(added.Elements, element => element.Type == ShaclElementMapper.ShapeType);
        Assert.Equal("res:http://example.org/PersonShape", card.Id);

        // The chip and the row travel inside the card, so exactly one element is drawn: no
        // element for the targeted class, none for the blank property shape.
        Assert.Single(added.Elements);
    }

    [Fact]
    public async Task TheSession_RefusesToStoreAPosition_WithoutARegistration()
    {
        var body = WriteBody();

        await using var session = Open(body, null);
        session.Baseline();

        var refusal = await session.MoveElementToAsync("res:http://example.org/PersonShape", 10, 20, TestContext.Current.CancellationToken);

        Assert.Contains("nowhere to store a position", refusal, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheSession_RefusesToMoveAnAnonymousShape_WithTheBoundarySentence()
    {
        // An anonymous node shape draws as a card, and takes only its computed place: its
        // ordinal belongs to this parse alone, so a stored position could not be trusted.
        var body = WriteBody("anon.ttl", """
            @prefix sh: <http://www.w3.org/ns/shacl#> .
            @prefix ex: <http://example.org/> .

            ex:S a sh:NodeShape ; sh:node [ sh:closed true ] .
            """);
        var registration = IoPath.Combine(_root, "anon.adp");
        await File.WriteAllTextAsync(registration, "w3c/shacl\r\nbody: anon.ttl\r\n", TestContext.Current.CancellationToken);

        await using var session = Open(body, registration);
        var added = Assert.IsType<DiagramAddDelta>(Assert.Single(session.Baseline()));
        var blank = Assert.Single(added.Elements, element => element.Id.StartsWith("blank:", StringComparison.Ordinal));

        var refusal = await session.MoveElementToAsync(blank.Id, 10, 20, TestContext.Current.CancellationToken);

        // The same sentence the writer and the gate use - one refusal, not three that drifted.
        Assert.Equal(ShaclRefusals.BlankRooted, refusal);
    }

    [Fact]
    public async Task TheSession_StoresAnIriCardsPosition_LeavingTheShapesFileUntouched()
    {
        var body = WriteBody();
        var before = await File.ReadAllTextAsync(body, TestContext.Current.CancellationToken);
        var registration = IoPath.Combine(_root, "shapes.adp");
        await File.WriteAllTextAsync(registration, "w3c/shacl\r\nbody: shapes.ttl\r\n", TestContext.Current.CancellationToken);

        await using var session = Open(body, registration);
        session.Baseline();

        var refusal = await session.MoveElementToAsync("res:http://example.org/PersonShape", 120, 240, TestContext.Current.CancellationToken);

        Assert.Equal("", refusal);
        Assert.Contains("layout:", await File.ReadAllTextAsync(registration, TestContext.Current.CancellationToken), StringComparison.Ordinal);
        Assert.Equal(before, await File.ReadAllTextAsync(body, TestContext.Current.CancellationToken)); // the RDF never moves
    }

    [Fact]
    public async Task StoringAPosition_UndoesToTheByte()
    {
        var body = WriteBody();
        var registration = IoPath.Combine(_root, "shapes.adp");
        await File.WriteAllTextAsync(registration, "w3c/shacl\r\nbody: shapes.ttl\r\n", TestContext.Current.CancellationToken);
        var before = await File.ReadAllTextAsync(registration, TestContext.Current.CancellationToken);

        await using var session = Open(body, registration);
        session.Baseline();

        Assert.Equal("", await session.MoveElementToAsync(
            "res:http://example.org/PersonShape", 120, 240, TestContext.Current.CancellationToken));
        Assert.NotEqual(before, await File.ReadAllTextAsync(registration, TestContext.Current.CancellationToken));

        // A layout edit rides the same history the triple writers do, so undoing it restores the
        // registration exactly - no residual `layout:` block, no trailing blank line left behind.
        var history = _provider.GetRequiredService<IHistoryStackStore>().Get(_root);
        Assert.True((await history.UndoAsync(TestContext.Current.CancellationToken)).IsSuccess);

        Assert.Equal(before, await File.ReadAllTextAsync(registration, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AnEditThroughOneReading_IsVisibleInTheOther_OnOneHistory()
    {
        // The half of coexistence that the drawing test cannot show: the two sessions are not
        // two copies of the file that happen to agree at open time. They share the parsed
        // document and the project's history, so a write through either is the other's next read
        // - and one undo puts both back.
        var body = WriteBody();
        var before = await File.ReadAllTextAsync(body, TestContext.Current.CancellationToken);

        await using var shapes = Open(body, null);
        var rdfFactory = _provider.GetServices<IDiagramSessionFactory>()
            .Single(candidate => candidate.Origin == ServiceCollectionAddRdfExtension.RdfOrigin);
        await using var graph = rdfFactory.Open(ShortGuid.NewShortGuid(), _root, body, null);

        shapes.Baseline();
        graph.Baseline();

        var history = _provider.GetRequiredService<IHistoryStackStore>().Get(_root);
        var added = await history.ExecuteAsync(
            new CreateShaclNodeShapeCommand(body, "http://example.org/AddressShape"),
            TestContext.Current.CancellationToken);
        Assert.True(added.IsSuccess, added.Error);

        // Both readings see it, each in its own vocabulary: a second card here, a further
        // subject there.
        var store = _provider.GetRequiredService<IRdfDocumentStore>();
        var model = store.GetOrLoad(body).Model;
        Assert.Equal(2, ShaclProjection.Project(model).Cards.Count);
        Assert.Contains(model.Triples, triple =>
            triple.Subject is IriTerm subject && subject.Iri == "http://example.org/AddressShape");

        Assert.True((await history.UndoAsync(TestContext.Current.CancellationToken)).IsSuccess);
        Assert.Equal(before, await File.ReadAllTextAsync(body, TestContext.Current.CancellationToken));
        Assert.Single(ShaclProjection.Project(store.GetOrLoad(body).Model).Cards);
    }

    [Fact]
    public async Task OneFileUnderTwoReadings_IsServedByOneStore()
    {
        // The coexistence rule: the anchor and this reading open the same bytes, each drawing
        // its own projection, through one parsed document.
        var body = WriteBody();

        await using var shapes = Open(body, null);
        var rdfFactory = _provider.GetServices<IDiagramSessionFactory>()
            .Single(candidate => candidate.Origin == ServiceCollectionAddRdfExtension.RdfOrigin);
        await using var graph = rdfFactory.Open(ShortGuid.NewShortGuid(), _root, body, null);

        var shapeElements = Assert.IsType<DiagramAddDelta>(Assert.Single(shapes.Baseline())).Elements;
        var graphElements = Assert.IsType<DiagramAddDelta>(Assert.Single(graph.Baseline())).Elements;

        // The shapes reading draws one card; the data-graph reading draws the resources and
        // their edges - more elements, and none of them a shapes card.
        Assert.Single(shapeElements);
        Assert.True(graphElements.Count > shapeElements.Count);
        Assert.DoesNotContain(graphElements, element => element.Type == ShaclElementMapper.ShapeType);
    }

    [Fact]
    public void TheDocumentFactory_WritesAStarterThatOpensAsACard()
    {
        var factory = _provider.GetServices<IDiagramDocumentFactory>()
            .Single(candidate => candidate.Origin == ServiceCollectionAddShaclExtension.ShaclOrigin);

        var text = factory.CreateEmptyDocument("Person");
        var projection = ShaclProjection.Project(RdfParser.Parse(RdfDocument.Parse(text)));

        var card = Assert.Single(projection.Cards);
        Assert.Equal("ex:PersonShape", card.Display);
        Assert.Single(card.Targets);
        Assert.Single(card.Rows);
    }
}
