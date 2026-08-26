using EtAlii.Adp.Backend.Diagrams;
using Google.Protobuf;
using Xunit;
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Diagram.WardleyMap.Tests;

public class WardleyElementMapperTests
{
    private readonly WardleyElementMapper _mapper = new(new WardleyIdentities());

    private static WardleyMap ParseText(string text) => WardleyParser.Parse(WardleyDocument.Parse(text));

    private static WardleyMap Fixture(string name) =>
        WardleyParser.Parse(WardleyDocument.Parse(
            File.ReadAllText(IoPath.Combine(AppContext.BaseDirectory, "Fixtures", name))));

    /// <summary>Maps a map with freshly reconciled identities, which is what a first open does.</summary>
    private IReadOnlyList<DiagramElement> Map(WardleyMap map) =>
        _mapper.Elements(map, WardleyIdentities.Reconcile(map, []));

    private static WardleyElementPayload PayloadOf(DiagramElement element) =>
        WardleyElementPayload.Parser.ParseFrom(element.Payload.Span);

    private static DiagramElement ElementNamed(IReadOnlyList<DiagramElement> elements, string name) =>
        elements.Single(element =>
            element.Type == WardleyElementTypes.Element && PayloadOf(element).Name == name);

    [Fact]
    public void Elements_PutsAComponentWhereTheDocumentSaid_ThroughTheAxisAndNoLayout()
    {
        // Arrange. Requirement 7.1 - the position is the author's claim, not a computed one.
        var map = ParseText("component Alpha [0.90, 0.10]\n");

        // Act.
        var element = ElementNamed(Map(map), "Alpha");

        // Assert. Genesis is left, the user need is top.
        Assert.Equal(0.10d, element.X);
        Assert.Equal(0.10d, element.Y, 10);
    }

    [Fact]
    public void Elements_CarriesTheDocumentsOwnNumbersAlongsideTheCanvasPosition()
    {
        // Arrange. The property grid shows and edits these; reconstructing them from the point
        // would be a second conversion, which Requirement 5.2 forbids.
        var map = ParseText("component Alpha [0.90, 0.10]\n");

        // Act.
        var payload = PayloadOf(ElementNamed(Map(map), "Alpha"));

        // Assert.
        Assert.Equal(0.90d, payload.Visibility);
        Assert.Equal(0.10d, payload.Maturity);
    }

    [Fact]
    public void Elements_ResolvesTheEvolutionStageBackendSide()
    {
        // Arrange. Requirement 8.2 - the client holds no copy of the boundaries.
        var map = ParseText("component Alpha [0.5, 0.75]\n");

        // Act.
        var payload = PayloadOf(ElementNamed(Map(map), "Alpha"));

        // Assert.
        Assert.Equal("Commodity (+utility)", payload.EvolutionStage);
    }

    [Fact]
    public void Elements_CarriesAllFiveDecoratorsAsASet()
    {
        // Arrange.
        var map = ParseText("component Payment [0.7, 0.72] (buy) (market)\n");

        // Act.
        var payload = PayloadOf(ElementNamed(Map(map), "Payment"));

        // Assert. A set, not a single choice - the format carries five independent booleans.
        Assert.Contains(WardleyDecorator.Buy, payload.Decorators);
        Assert.Contains(WardleyDecorator.Market, payload.Decorators);
        Assert.Equal(2, payload.Decorators.Count);
    }

    [Fact]
    public void Elements_AttachesAnEvolveToTheComponentItNames_WithBothStages()
    {
        // Arrange. Requirement 6.1 - the pair is the point of the statement.
        var map = ParseText("component Datacentre [0.15, 0.55]\nevolve Datacentre->Cloud Hosting 0.83\n");

        // Act.
        var payload = PayloadOf(ElementNamed(Map(map), "Datacentre"));

        // Assert.
        Assert.NotNull(payload.Evolve);
        Assert.Equal(0.83d, payload.Evolve.Maturity);
        Assert.Equal("Cloud Hosting", payload.Evolve.OverrideName);
        Assert.Equal("Commodity (+utility)", payload.Evolve.EvolutionStage);
        Assert.Equal("Product (+rental)", payload.EvolutionStage);
    }

    [Fact]
    public void Elements_ResolvesLinkEndpointsToIds()
    {
        // Arrange.
        var map = ParseText("component A [0.9, 0.1]\ncomponent B [0.5, 0.5]\nA->B\n");
        var elements = Map(map);

        // Act.
        var link = elements.Single(element => element.Type == WardleyElementTypes.Link);
        var payload = WardleyLinkPayload.Parser.ParseFrom(link.Payload.Span);

        // Assert.
        Assert.Equal(ElementNamed(elements, "A").Id, payload.SourceId);
        Assert.Equal(ElementNamed(elements, "B").Id, payload.TargetId);
    }

    [Fact]
    public void Elements_StillSendsALinkWhoseEndpointDoesNotExist_CarryingTheNameAsWritten()
    {
        // Arrange. Requirement 3.5 - a dangling link must not stop the map rendering, and
        // Requirement 14.2 needs the name to report it by.
        var map = ParseText("component A [0.9, 0.1]\nA->Nowhere\n");

        // Act.
        var link = Map(map).Single(element => element.Type == WardleyElementTypes.Link);
        var payload = WardleyLinkPayload.Parser.ParseFrom(link.Payload.Span);

        // Assert.
        Assert.Equal("", payload.TargetId);
        Assert.Equal("Nowhere", payload.TargetName);
    }

    [Fact]
    public void Elements_GivesAPipelineChildItsParentsVisibility()
    {
        // Arrange. Requirement 7.4 - the format gives a child no visibility of its own.
        const string text = """
            component Kettle [0.43, 0.35]
            pipeline Kettle
            {
              component Electric [0.63]
            }
            """;
        var map = ParseText(text);
        var elements = Map(map);

        // Act.
        var child = PayloadOf(ElementNamed(elements, "Electric"));

        // Assert.
        Assert.Equal(0.43d, child.Visibility);
        Assert.Equal(0.63d, child.Maturity);
        Assert.Equal(ElementNamed(elements, "Kettle").Id, child.PipelineParentId);
    }

    [Fact]
    public void Elements_SendsEveryOccurrenceOfAMultiPositionAnnotation()
    {
        // Arrange. Requirement 6.8's headline case, now on the wire: a core Element has one
        // position, and none of these may be lost.
        var map = ParseText("annotation 1 [[0.43,0.49],[0.08,0.79]] Two places\n");

        // Act.
        var element = Map(map).Single(candidate => candidate.Type == WardleyElementTypes.Annotation);
        var payload = WardleyAnnotationPayload.Parser.ParseFrom(element.Payload.Span);

        // Assert.
        Assert.Equal(2, payload.Occurrences.Count);
        Assert.Equal(0.49d, element.X);
        Assert.Equal(0.49d, payload.Occurrences[0].X);
        Assert.Equal(0.79d, payload.Occurrences[1].X);
    }

    [Fact]
    public void Elements_AlwaysIncludesExactlyOneEvolutionAxis()
    {
        // Act.
        var elements = Map(Fixture("tea-shop.owm"));

        // Assert. Requirement 8.2 - sent as data so the client holds no constants.
        var axis = Assert.Single(elements, element => element.Type == WardleyElementTypes.EvolutionAxis);
        var payload = WardleyEvolutionAxisPayload.Parser.ParseFrom(axis.Payload.Span);
        Assert.Equal(4, payload.Stages.Count);
        Assert.Equal("Genesis", payload.Stages[0].Label);
        Assert.Equal(WardleyEvolution.CustomBuilt, payload.Stages[0].End);
        Assert.Equal("Tea shop", payload.Title);
    }

    [Fact]
    public void Elements_IncludesTheAxisEvenForAnEmptyMap()
    {
        // Act. An empty map opens showing its axes and nothing else (Requirement 1.4).
        var elements = Map(WardleyMap.Empty);

        // Assert.
        Assert.Equal(WardleyElementTypes.EvolutionAxis, Assert.Single(elements).Type);
    }

    [Fact]
    public void Elements_ResolvesASubmapsUrlToItsAddress()
    {
        // Arrange.
        var map = Fixture("submaps-and-urls.owm");

        // Act.
        var payload = PayloadOf(ElementNamed(Map(map), "Fulfilment"));

        // Assert.
        Assert.Equal("fulfilmentMap", payload.UrlName);
        Assert.Equal("https://onlinewardleymaps.com/#clone:example-fulfilment", payload.UrlAddress);
    }

    [Fact]
    public void Elements_CoversEveryFixtureWithoutThrowing()
    {
        // Act and assert. Nothing in the corpus - dangling links, unknown statements, empty
        // pipelines - may take the mapper down.
        foreach (var path in Directory.EnumerateFiles(IoPath.Combine(AppContext.BaseDirectory, "Fixtures"), "*.owm"))
        {
            var map = WardleyParser.Parse(WardleyDocument.Parse(File.ReadAllText(path)));
            Assert.NotEmpty(Map(map));
        }
    }

    [Fact]
    public void Diff_ReportsNothingForTwoIdenticalRenderings()
    {
        // Arrange.
        var map = Fixture("tea-shop.owm");
        var identities = WardleyIdentities.Reconcile(map, []);

        // Act.
        var deltas = _mapper.Diff(_mapper.Elements(map, identities), _mapper.Elements(map, identities));

        // Assert.
        Assert.Empty(deltas);
    }

    [Fact]
    public void Diff_ExpressesAMoveAsAnAdd_NotARemoveAndAdd()
    {
        // Arrange. Requirement 10.3 - an edit is an add carrying the element in its new state,
        // because a remove-then-add makes the element momentarily absent and drops selection.
        var before = ParseText("component Alpha [0.9, 0.1]\n");
        var identities = WardleyIdentities.Reconcile(before, []);
        var after = ParseText("component Alpha [0.5, 0.5]\n");

        // Act.
        var deltas = _mapper.Diff(_mapper.Elements(before, identities), _mapper.Elements(after, identities));

        // Assert.
        var add = Assert.IsType<DiagramAddDelta>(Assert.Single(deltas));
        Assert.Single(add.Elements);
    }

    [Fact]
    public void Diff_ReportsARemoveForAnElementThatIsGone()
    {
        // Arrange.
        var before = ParseText("component Alpha [0.9, 0.1]\ncomponent Beta [0.5, 0.5]\n");
        var identities = WardleyIdentities.Reconcile(before, []);
        var after = ParseText("component Alpha [0.9, 0.1]\n");

        // Act.
        var deltas = _mapper.Diff(
            _mapper.Elements(before, identities),
            _mapper.Elements(after, WardleyIdentities.Reconcile(after, identities)));

        // Assert.
        Assert.Single(deltas.OfType<DiagramRemoveDelta>().Single().ElementIds);
    }

    [Fact]
    public void Group_ExpressesAPipelineAsTheContractsGrouping()
    {
        // Arrange. Requirement 10.4 - the second independent use of Group/Ungroup after
        // mindmap folding, with no new Delta action invented.
        const string text = """
            component Kettle [0.43, 0.35]
            pipeline Kettle
            {
              component Campfire [0.15]
              component Electric [0.63]
            }
            """;
        var map = ParseText(text);

        // Act.
        var deltas = _mapper.Group(map, WardleyIdentities.Reconcile(map, []));

        // Assert.
        var group = Assert.IsType<DiagramGroupDelta>(Assert.Single(deltas));
        Assert.Equal(2, group.SourceElementIds.Count);
        Assert.Equal("Kettle", PayloadOf(group.GroupElement).Name);
    }

    [Fact]
    public void Group_SkipsAPipelineWhoseParentDoesNotExist()
    {
        // Arrange. A problem the validator reports; there is nothing here to group under.
        var map = ParseText("pipeline Missing\n{\n  component Child [0.5]\n}\n");

        // Act.
        var deltas = _mapper.Group(map, WardleyIdentities.Reconcile(map, []));

        // Assert.
        Assert.Empty(deltas);
    }

    [Fact]
    public void Elements_GivesEveryElementANonEmptyId()
    {
        // Act.
        var elements = Map(Fixture("strategy-vocabulary.owm"));

        // Assert. An element with no id cannot be selected, moved or reported against.
        Assert.All(elements, element => Assert.NotEmpty(element.Id));
    }
}
