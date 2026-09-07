using EtAlii.Adp.Common;
using EtAlii.Adp.Hierarchy;
using EtAlii.Adp.History;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.Rdf.Tests;

/// <summary>
/// The ontology session: a registered ontology opens with the VOWL elements and the overlaid
/// layout, expression nodes refuse repositioning with the boundary's sentence, IRI-derived ids
/// reposition undoably without the body changing by a byte, and two readings of one file share
/// one store and one history (owl-diagram Requirements 3.2, 4.1, 8.1-8.4).
/// </summary>
public class OwlSessionTests : IDisposable
{
    private const string Ns = "http://example.org/pizza#";

    private readonly string _root;
    private readonly ServiceProvider _provider;

    public OwlSessionTests()
    {
        _root = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _provider = new ServiceCollection()
            .AddSingleton<IReadOnlyList<DiagramDefinition>>(Diagram.Definitions)
            .AddCommands().AddHierarchyCommandHandlers()
            .AddRdf()
            .BuildServiceProvider();
    }

    public void Dispose()
    {
        _provider.Dispose();
        TestFolder.TryDelete(_root);
    }

    private string CopyFixture(string name)
    {
        var destination = IoPath.Combine(_root, name);
        File.Copy(IoPath.Combine(AppContext.BaseDirectory, "Fixtures", name), destination);
        return destination;
    }

    private string WriteRegistration(string bodyName, string extra = "")
    {
        var path = IoPath.Combine(_root, "ontology.adp");
        File.WriteAllText(path, $"w3c/owl\r\nbody: {bodyName}\r\n{extra}");
        return path;
    }

    private IDiagramSession Open(DiagramOrigin origin, string bodyPath, string? registrationPath)
    {
        var factory = _provider.GetServices<IDiagramSessionFactory>()
            .Single(candidate => candidate.Origin == origin);
        return factory.Open(ShortGuid.NewShortGuid(), _root, bodyPath, registrationPath);
    }

    private IDiagramSession OpenOwl(string bodyPath, string? registrationPath) =>
        Open(ServiceCollectionAddRdfExtension.OwlOrigin, bodyPath, registrationPath);

    private static IReadOnlyList<DiagramElement> ElementsOf(IDiagramSession session)
    {
        var baseline = Assert.Single(session.Baseline());
        return Assert.IsType<DiagramAddDelta>(baseline).Elements.ToList();
    }

    [Fact]
    public async Task ARegisteredOntology_OpensWithTheVowlElements()
    {
        // Arrange.
        var body = CopyFixture("owl-ontology.ttl");

        // Act.
        await using var session = OpenOwl(body, WriteRegistration("owl-ontology.ttl"));
        var elements = ElementsOf(session);

        // Assert: class nodes, axiom edges and expression nodes, in this reading's own types.
        Assert.Contains(elements, element => element.Id == $"res:{Ns}Pizza" && element.Type == OwlElementMapper.NodeType);
        Assert.Contains(elements, element => element.Type == OwlElementMapper.EdgeType);
        Assert.Contains(elements, element => element.Type == OwlElementMapper.ExpressionType);
        Assert.DoesNotContain(elements, element => element.Type == OwlElementMapper.TruncationType);
    }

    [Fact]
    public async Task AStoredPosition_WinsForAClass_AndNeverForAnExpression()
    {
        // Arrange: the registration stores a position for a class AND for an expression node.
        var body = CopyFixture("owl-ontology.ttl");
        var expressionId = $"expr:{Ns}Vegetarian|http://www.w3.org/2000/01/rdf-schema#subClassOf|1";
        var adp = WriteRegistration(
            "owl-ontology.ttl",
            $"layout:\r\n  res:{Ns}Pizza: 555 666\r\n  {expressionId}: 111 222\r\n");

        // Act.
        await using var session = OpenOwl(body, adp);
        var elements = ElementsOf(session);

        // Assert: the class takes its authored place; the expression keeps its computed one -
        // its id does not survive an edit, the identity boundary's reason (Requirement 3.2).
        var pizza = elements.Single(element => element.Id == $"res:{Ns}Pizza");
        Assert.Equal((555d, 666d), (pizza.X, pizza.Y));
        var expression = elements.Single(element => element.Id == expressionId);
        Assert.NotEqual((111d, 222d), (expression.X, expression.Y));
    }

    [Fact]
    public async Task ARepositionLandsInTheRegistration_IsOneUndoAway_AndTheBodyNeverChanges()
    {
        // Arrange.
        var body = CopyFixture("owl-ontology.ttl");
        var bodyBytes = await File.ReadAllBytesAsync(body, TestContext.Current.CancellationToken);
        var adp = WriteRegistration("owl-ontology.ttl");
        var adpBefore = await File.ReadAllTextAsync(adp, TestContext.Current.CancellationToken);

        // Act.
        await using var session = OpenOwl(body, adp);
        var refusal = await session.MoveElementToAsync($"res:{Ns}Pizza", 120, 240, TestContext.Current.CancellationToken);

        // Assert.
        Assert.Equal("", refusal);
        Assert.Equal(new RegistrationPosition(120, 240), RegistrationLayout.Read(adp)[$"res:{Ns}Pizza"]);
        Assert.Equal(bodyBytes, await File.ReadAllBytesAsync(body, TestContext.Current.CancellationToken));
        await _provider.GetRequiredService<IHistoryStackStore>().Get(_root)
            .UndoAsync(TestContext.Current.CancellationToken);
        Assert.Equal(adpBefore, await File.ReadAllTextAsync(adp, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task TheRefusals_AnswerWithTheirSentences()
    {
        // Arrange.
        var body = CopyFixture("owl-ontology.ttl");

        // A bare file has nowhere to store a position; registering lifts it.
        await using (var bare = OpenOwl(body, null))
        {
            var refusal = await bare.MoveElementToAsync($"res:{Ns}Pizza", 1, 2, TestContext.Current.CancellationToken);
            Assert.Contains("Register the file", refusal);
        }

        await using var session = OpenOwl(body, WriteRegistration("owl-ontology.ttl"));

        // An expression node's id does not survive an edit - the boundary's sentence (3.2).
        var expression = await session.MoveElementToAsync(
            $"expr:{Ns}Vegetarian|http://www.w3.org/2000/01/rdf-schema#subClassOf|1", 1, 2, TestContext.Current.CancellationToken);
        Assert.Contains("does not survive an edit", expression);

        // A materialized anchor is drawn where its property needs it.
        var anchor = await session.MoveElementToAsync($"thing:{Ns}toppingOf|domain", 1, 2, TestContext.Current.CancellationToken);
        Assert.Contains("cannot be arranged", anchor);

        // An edge follows its endpoints.
        var edge = await session.MoveElementToAsync("edge:whatever", 1, 2, TestContext.Current.CancellationToken);
        Assert.Contains("not something this diagram can move", edge);
    }

    [Fact]
    public async Task TwoReadingsOfOneFile_ShareOneDocument()
    {
        // Arrange: the same body open under w3c/rdf and w3c/owl at once (Requirement 8.4).
        var body = CopyFixture("owl-ontology.ttl");
        await using var rdfSession = Open(ServiceCollectionAddRdfExtension.RdfOrigin, body, null);
        await using var owlSession = OpenOwl(body, null);
        _ = ElementsOf(rdfSession);
        _ = ElementsOf(owlSession);

        var rdfHeard = false;
        var owlHeard = false;
        rdfSession.Changed += (_, _) => rdfHeard = true;
        owlSession.Changed += (_, _) => owlHeard = true;

        // Act: a change to the file, reloaded through the one store both readings share.
        await File.AppendAllTextAsync(body, ":Calzone a owl:Class .\r\n", TestContext.Current.CancellationToken);
        _provider.GetRequiredService<IRdfDocumentStore>().Reload(body);

        // Assert: both sessions heard the same document change.
        Assert.True(rdfHeard);
        Assert.True(owlHeard);
    }

    [Fact]
    public async Task AViewportChange_AddsWhatCameIntoViewAndRemovesWhatLeft()
    {
        // Arrange.
        // The behavioural definition of the mechanism (view-delta-adoption Requirement 1.3): a
        // view CHANGE produces deltas. This is the test that fails against a session whose
        // UpdateView returns an empty list, which no client-side assertion can catch.
        var body = CopyFixture("owl-ontology.ttl");
        await using var session = OpenOwl(body, WriteRegistration("owl-ontology.ttl"));
        var drawn = ElementsOfBaseline(session);
        var anchor = drawn.OrderBy(element => element.Y).ThenBy(element => element.X).First();

        // Act.
        var narrowed = session.UpdateView(new DiagramViewport(anchor.X, anchor.Y, anchor.X + 1, anchor.Y + 1));
        var widened = session.UpdateView(DiagramViewport.Unbounded);

        // Assert.
        var removed = narrowed.OfType<DiagramRemoveDelta>().SelectMany(delta => delta.ElementIds).ToHashSet(StringComparer.Ordinal);
        Assert.NotEmpty(removed);
        Assert.DoesNotContain(anchor.Id, removed);

        var restored = widened.OfType<DiagramAddDelta>().SelectMany(delta => delta.Elements).Select(element => element.Id).ToHashSet(StringComparer.Ordinal);
        Assert.Subset(restored, removed);

        // Add before Remove - what the reference implementations emit, and what this module's
        // own mapper already produced. Requirement 4.3 anticipated the opposite.
        var kinds = widened.Select(delta => delta is DiagramAddDelta ? 0 : 1).ToList();
        Assert.Equal(kinds.OrderBy(kind => kind).ToList(), kinds);
    }

    [Fact]
    public async Task AnUnchangedViewport_SaysNothingTwice()
    {
        // Arrange.
        // A settled view that has not moved is not news; without this, any re-render that
        // re-reported the same rectangle would re-send the whole diagram.
        var body = CopyFixture("owl-ontology.ttl");
        await using var session = OpenOwl(body, WriteRegistration("owl-ontology.ttl"));
        session.UpdateView(DiagramViewport.Unbounded);

        // Act.
        var again = session.UpdateView(DiagramViewport.Unbounded);

        // Assert.
        Assert.Empty(again);
    }

    [Fact]
    public async Task PanningDoesNotMoveTheElementsItBringsIntoView()
    {
        // Arrange.
        // The layout is computed over the whole document and only then filtered. Computed over
        // the visible set instead, it would repack as the reader panned and the diagram would
        // crawl under them.
        var body = CopyFixture("owl-ontology.ttl");
        await using var session = OpenOwl(body, WriteRegistration("owl-ontology.ttl"));
        var atOpen = ElementsOfBaseline(session).ToDictionary(element => element.Id, element => (element.X, element.Y), StringComparer.Ordinal);

        // Act.
        session.UpdateView(new DiagramViewport(0, 0, 1, 1));
        var readmitted = session.UpdateView(DiagramViewport.Unbounded)
            .OfType<DiagramAddDelta>()
            .SelectMany(delta => delta.Elements)
            .ToList();

        // Assert.
        Assert.NotEmpty(readmitted);
        foreach (var element in readmitted)
        {
            Assert.Equal(atOpen[element.Id], (element.X, element.Y));
        }
    }

    /// <summary>The baseline's elements - what the connection holds before any view is reported.</summary>
    private static IReadOnlyList<DiagramElement> ElementsOfBaseline(IDiagramSession session) =>
        session.Baseline().OfType<DiagramAddDelta>().SelectMany(delta => delta.Elements).ToList();
}

/// <summary>
/// The w3c/owl definition itself: never routed from a bare body, suggested off its marker
/// (owl-diagram Requirements 8.1, 8.2).
/// </summary>
public class OwlDefinitionTests
{
    [Fact]
    public void TheDefinition_SharesTheFamilyExtensions_AndNeverClaimsABareBody()
    {
        // Arrange & act.
        var owl = Diagram.Owl;

        // Assert: same extensions as the anchor, shared - so a bare .ttl stays w3c/rdf's.
        Assert.Equal(".ttl", owl.Extension);
        Assert.Equal(".nt", owl.AlternateExtension);
        Assert.True(owl.SharedExtension);
        Assert.False(Diagram.Rdf.SharedExtension);
        Assert.Contains(owl, Diagram.Definitions);

        // The description carries the asserted-not-inferred stance (Requirement 8.1).
        Assert.Contains("asserts", owl.Description);
    }

    [Fact]
    public void TheMarkerTest_SuggestsExactlyTheFilesThatCarryTheMarker()
    {
        // Arrange & act & assert (Requirement 8.2).
        Assert.NotNull(Diagram.Owl.SuggestsBody);
        Assert.True(Diagram.Owl.SuggestsBody!("<http://example.org/o> a owl:Ontology ."));
        Assert.True(Diagram.Owl.SuggestsBody!("<http://example.org/o> a <http://www.w3.org/2002/07/owl#Ontology> ."));
        Assert.False(Diagram.Owl.SuggestsBody!("ex:a foaf:knows ex:b ."));
    }
}
