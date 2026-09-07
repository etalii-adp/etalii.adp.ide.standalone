using Xunit;

using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Diagram.HelmCharts.Tests;

/// <summary>
/// Projection to the wire (Requirement 4.4): stable ids, typed elements, kind-specific
/// payloads, and a Diff that only ever removes and adds.
/// </summary>
public class HelmElementMapperTests
{
    private static (HelmChart Chart, HelmGraph Graph) WellFormed()
    {
        var chart = new HelmChartReader().Read(IoPath.Combine(AppContext.BaseDirectory, "Fixtures", "well-formed"));
        return (chart, HelmGraph.Derive(chart));
    }

    private static IReadOnlyDictionary<string, HelmBox> BoxesFor(HelmGraph graph)
    {
        // Hand-built boxes: layout is the next task's concern, and the mapper must not care.
        var boxes = new Dictionary<string, HelmBox>(StringComparer.Ordinal);
        var index = 0;
        foreach (var node in graph.Nodes)
        {
            boxes[node.Id] = new HelmBox(index * 100, index * 10, 90, 40);
            index++;
        }

        return boxes;
    }

    [Fact]
    public void EveryNodeAndEveryEdge_BecomesOneElement()
    {
        // Arrange.
        var (chart, graph) = WellFormed();

        // Act.
        var elements = new HelmElementMapper().Elements(chart, graph, BoxesFor(graph));

        // Assert.
        Assert.Equal(graph.Nodes.Count + graph.Edges.Count, elements.Count);
        Assert.All(elements, element => Assert.False(string.IsNullOrEmpty(element.Id)));
    }

    [Fact]
    public void NodesTakeTheirBoxes_AndTheirSizesTravel()
    {
        // Arrange.
        var (chart, graph) = WellFormed();
        var boxes = BoxesFor(graph);

        // Act.
        var elements = new HelmElementMapper().Elements(chart, graph, boxes);

        // Assert.
        var chartElement = Assert.Single(elements, element => element.Id == "chart");
        Assert.Equal(boxes["chart"].X, chartElement.X);
        Assert.Equal(boxes["chart"].Y, chartElement.Y);
        var payload = Payload(chartElement);
        Assert.Equal(90, payload.Width);
        Assert.Equal(40, payload.Height);
    }

    [Fact]
    public void TypesFollowTheKind_AndPartialsGetTheirOwn()
    {
        // Arrange.
        var (chart, graph) = WellFormed();

        // Act.
        var elements = new HelmElementMapper().Elements(chart, graph, BoxesFor(graph));
        var byId = elements.ToDictionary(element => element.Id);

        // Assert.
        Assert.Equal(HelmElementMapper.ChartType, byId["chart"].Type);
        Assert.Equal(HelmElementMapper.ValuesType, byId["values:values.yaml"].Type);
        Assert.Equal(HelmElementMapper.TemplateType, byId["tpl:templates/deployment.yaml"].Type);
        Assert.Equal(HelmElementMapper.PartialType, byId["tpl:templates/_helpers.tpl"].Type);
        Assert.Equal(HelmElementMapper.DependencyType, byId["dep:cache"].Type);
        Assert.Equal(HelmElementMapper.SubchartType, byId["sub:charts/redis"].Type);
        Assert.Equal(HelmElementMapper.SchemaType, byId["schema:values.schema.json"].Type);
        Assert.Equal(HelmElementMapper.LockType, byId["lock:Chart.lock"].Type);
        Assert.Equal(HelmElementMapper.CrdsType, byId["crds"].Type);
    }

    [Fact]
    public void ADependencyPayload_CarriesResolutionConditionAndTheLockPin()
    {
        // Arrange.
        var (chart, graph) = WellFormed();

        // Act.
        var elements = new HelmElementMapper().Elements(chart, graph, BoxesFor(graph));
        var cache = Payload(Assert.Single(elements, element => element.Id == "dep:cache"));
        var postgres = Payload(Assert.Single(elements, element => element.Id == "dep:postgres"));

        // Assert.
        Assert.Equal("redis", cache.Dependency.ChartName);
        Assert.Equal("cache", cache.Dependency.Alias);
        Assert.True(cache.Dependency.Resolved);
        Assert.Equal(Wire.HelmConditionState.ConditionOn, cache.Dependency.ConditionState);
        // The lock pins by chart name: redis is pinned 17.3.2 even though the alias is cache.
        Assert.Equal("17.3.2", cache.Dependency.PinnedVersion);
        Assert.False(postgres.Dependency.Resolved);
        Assert.Equal(Wire.HelmConditionState.ConditionNone, postgres.Dependency.ConditionState);
    }

    [Fact]
    public void AnUnreadableValuesFile_IsMarkedWithTheParsersWords()
    {
        // Arrange.
        var chart = new HelmChartReader().Read(IoPath.Combine(AppContext.BaseDirectory, "Fixtures", "broken"));
        var graph = HelmGraph.Derive(chart);

        // Act.
        var elements = new HelmElementMapper().Elements(chart, graph, BoxesFor(graph));
        var values = Payload(Assert.Single(elements, element => element.Id == "values:values.yaml"));

        // Assert.
        Assert.True(values.Unreadable);
        Assert.True(values.FailureLine > 0);
        Assert.NotEqual(string.Empty, values.FailureMessage);
    }

    [Fact]
    public void AnEdgeElement_CarriesItsWireEdge()
    {
        // Arrange.
        var (chart, graph) = WellFormed();

        // Act.
        var elements = new HelmElementMapper().Elements(chart, graph, BoxesFor(graph));
        var open = elements
            .Select(Payload)
            .Single(payload => payload.Kind == Wire.HelmElementKind.Edge
                               && payload.Edge.Kind == Wire.HelmEdgeKind.Resolves
                               && payload.Edge.OpenEnd);

        // Assert.
        Assert.Equal("dep:postgres", open.Edge.SourceId);
        Assert.Equal(string.Empty, open.Edge.TargetId);
    }

    [Fact]
    public void Diff_RemovesWhatVanished_ThenUpsertsWhatRemains()
    {
        // Arrange.
        var (chart, graph) = WellFormed();
        var mapper = new HelmElementMapper();
        var before = mapper.Elements(chart, graph, BoxesFor(graph));
        // The folder lost its override layer: two elements vanish (the node and its edge).
        var after = before
            .Where(element => element.Id != "values:values-staging.yaml"
                              && !element.Id.Contains("values:values-staging.yaml", StringComparison.Ordinal))
            .ToArray();

        // Act.
        var deltas = mapper.Diff(before, after);

        // Assert.
        Assert.Equal(2, deltas.Count);
        var remove = Assert.IsType<DiagramRemoveDelta>(deltas[0]);
        Assert.Equal(2, remove.ElementIds.Count);
        var add = Assert.IsType<DiagramAddDelta>(deltas[1]);
        Assert.Equal(after.Length, add.Elements.Count);
        // Never a group or ungroup: nothing here folds, nothing here is edited.
    }

    /// <summary>
    /// <see cref="HelmElementMapper.Elements"/> is now reachable only from tests - the session
    /// goes through <see cref="HelmElementMapper.Visible"/> for its baseline as well as its view
    /// updates - and its body is very nearly the unbounded case of that method written a second
    /// time. Two bodies with no stated relationship drift, and the one nothing exercises drifts
    /// first, so the relationship is stated here instead.
    /// </summary>
    [Fact]
    public void TheUnfilteredMapping_MatchesTheUnboundedViewport_OnARealChart()
    {
        // Arrange: computed boxes rather than the hand-built ones, because what makes the two
        // agree is a property of the layout - it places every node - and hand-built boxes would
        // assume the very thing worth checking.
        var (chart, graph) = WellFormed();
        var boxes = HelmLayout.Compute(chart, graph);
        var mapper = new HelmElementMapper();

        // Act & assert: the same elements, in the same order.
        Assert.Equal(
            mapper.Elements(chart, graph, boxes).Select(element => element.Id),
            mapper.Visible(chart, graph, boxes, DiagramViewport.Unbounded).Select(element => element.Id));
    }

    /// <summary>
    /// And where they part, which is why the two are not simply collapsed into one.
    /// </summary>
    /// <remarks>
    /// An unbounded viewport is not quite "everything": <see cref="HelmElementMapper.Visible"/>
    /// admits a node only if the layout gave it a box, and an edge only if both its ends were
    /// admitted. On a well-formed chart neither exclusion bites - the test above is the proof -
    /// but they are real, so replacing <c>Elements</c> with a call to <c>Visible</c> would
    /// quietly change what a malformed graph maps to rather than being the pure refactor it
    /// looks like.
    /// </remarks>
    [Fact]
    public void TheTwoPartCompany_WhenTheLayoutPlacedNothing()
    {
        // Arrange: the same graph, with every box withheld.
        var (chart, graph) = WellFormed();
        var none = new Dictionary<string, HelmBox>(StringComparer.Ordinal);
        var mapper = new HelmElementMapper();

        // Act.
        var unfiltered = mapper.Elements(chart, graph, none);
        var unbounded = mapper.Visible(chart, graph, none, DiagramViewport.Unbounded);

        // Assert: the unfiltered mapping still draws the chart; the viewport one has nothing it
        // can place.
        Assert.NotNull(none);
        Assert.NotEmpty(unfiltered);
        Assert.Empty(unbounded);
    }

    private static Wire.HelmElementPayload Payload(DiagramElement element) =>
        Wire.HelmElementPayload.Parser.ParseFrom(element.Payload.Span);
}
