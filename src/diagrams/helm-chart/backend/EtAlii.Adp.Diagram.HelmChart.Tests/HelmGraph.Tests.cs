using Xunit;

using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Diagram.HelmChart.Tests;

/// <summary>
/// The nodes, and the fire and non-fire cases of the two edge families the graph still makes, <c>Resolves</c>
/// and <c>Includes</c> (Requirement 5), over the well-formed fixture where the families interlock and over
/// constructed models for the corners.
/// </summary>
/// <remarks>
/// <c>Declares</c>, <c>Overrides</c> and <c>Configures</c> are derived by the bundled definition
/// (helm-chart-disl-fbl, task 6); their cases here were replaced by the parity transcript, which records every
/// relation of every chart of the corpus with its ends and label.
/// </remarks>
public class HelmGraphTests
{
    private static HelmGraph WellFormed() =>
        HelmGraph.Derive(new HelmChartReader().Read(
            IoPath.Combine(AppContext.BaseDirectory, "Fixtures", "well-formed")));

    [Fact]
    public void Resolves_LandsOnTheVendoredEntry_OrStaysAnOpenEnd()
    {
        // Arrange & Act.
        var graph = WellFormed();

        // Assert.
        // redis is vendored (matched via the chart name even though the alias is "cache");
        // postgres is Unvendored - drawn as a marked open end, not a finding.
        var resolves = graph.Edges.Where(edge => edge.Kind == HelmEdgeKind.Resolves).ToArray();
        Assert.Contains(resolves, edge => edge is { SourceId: "dep:cache", TargetId: "sub:charts/redis", OpenEnd: false });
        Assert.Contains(resolves, edge => edge is { SourceId: "dep:postgres", TargetId.Length: 0, OpenEnd: true });
    }

    [Fact]
    public void Includes_LandsOnTheDefiningPartial()
    {
        // Arrange & Act.
        var graph = WellFormed();

        // Assert.
        var includes = graph.Edges
            .Where(edge => edge is { Kind: HelmEdgeKind.Includes, SourceId: "tpl:templates/deployment.yaml" })
            .ToArray();
        Assert.Equal(2, includes.Length);
        Assert.All(includes, edge =>
        {
            if (edge == null!)
            {
                throw new ArgumentNullException(nameof(edge));
            }

            Assert.Equal("tpl:templates/_helpers.tpl", edge.TargetId);
            Assert.False(edge.OpenEnd);
        });
        Assert.Equal(["shop.fullname", "shop.labels"], includes.Select(edge => edge.Label).Order(StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void AnIncludeNoLocalPartialDefines_IsAnOpenEndNotAFinding()
    {
        // Arrange.
        // A library chart provides these at render time, so the reference is a marked
        // unknown - the edge keeps its identity through the label (R5.6).
        var template = new TemplateFile(
            "templates/deployment.yaml", TemplateRole.Manifest, new TemplateFacts([], [], [], ["common.labels"]));
        var chart = Minimal() with { Templates = [template] };

        // Act.
        var graph = HelmGraph.Derive(chart);

        // Assert.
        var open = Assert.Single(graph.Edges, edge => edge.Kind == HelmEdgeKind.Includes);
        Assert.True(open.OpenEnd);
        Assert.Equal(string.Empty, open.TargetId);
        Assert.Equal("common.labels", open.Label);
        Assert.Contains("common.labels", open.Id, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryNodeKind_AppearsForTheWellFormedFixture()
    {
        // Arrange & Act.
        var graph = WellFormed();

        // Assert.
        var kinds = graph.Nodes.Select(node => node.Kind).Distinct().ToArray();
        Assert.Contains(HelmNodeKind.Chart, kinds);
        Assert.Contains(HelmNodeKind.Values, kinds);
        Assert.Contains(HelmNodeKind.Schema, kinds);
        Assert.Contains(HelmNodeKind.Template, kinds);
        Assert.Contains(HelmNodeKind.Crds, kinds);
        Assert.Contains(HelmNodeKind.Dependency, kinds);
        Assert.Contains(HelmNodeKind.Subchart, kinds);
        Assert.Contains(HelmNodeKind.Lock, kinds);
    }

    [Fact]
    public void ASealedArchive_IsAnArchiveNode()
    {
        // Arrange & Act.
        var graph = HelmGraph.Derive(new HelmChartReader().Read(
            IoPath.Combine(AppContext.BaseDirectory, "Fixtures", "unconventional")));

        // Assert.
        var archive = Assert.Single(graph.Nodes, node => node.Kind == HelmNodeKind.Archive);
        Assert.Equal("sealed", archive.Name);
        Assert.Equal("charts/sealed-9.9.9.tgz", archive.RelativePath);
    }

    [Fact]
    public void UndeclaredVendoredContent_IsDrawnAndAccounted()
    {
        // Arrange & Act.
        // The unconventional fixture declares only "deep"; the sealed archive is undeclared -
        // still a node, and named in the resolution for validation to warn about.
        var graph = HelmGraph.Derive(new HelmChartReader().Read(
            IoPath.Combine(AppContext.BaseDirectory, "Fixtures", "unconventional")));

        // Assert.
        Assert.NotNull(graph.Node("tgz:charts/sealed-9.9.9.tgz"));
        Assert.Equal("sealed", Assert.Single(graph.Resolution.Undeclared).EntryName);
    }

    [Fact]
    public void NotAChart_IsAnEmptyGraph()
    {
        // Arrange & Act.
        var graph = HelmGraph.Derive(HelmChart.NotAChart);

        // Assert.
        Assert.Empty(graph.Nodes);
        Assert.Empty(graph.Edges);
    }

    [Fact]
    public void TwoIncludesOfDifferentNames_OnOnePartial_AreTwoEdgesWithTwoIds()
    {
        // Arrange & Act.
        // The composite edge id (target AND label): the mapper found the collision when both
        // includes of deployment.yaml keyed to the same partial - this pins the fix.
        var graph = WellFormed();

        // Assert.
        var ids = graph.Edges
            .Where(edge => edge.Kind == HelmEdgeKind.Includes)
            .Select(edge => edge.Id)
            .ToArray();
        Assert.Equal(ids.Length, ids.Distinct(StringComparer.Ordinal).Count());
        // And across the whole graph: element identity is what the wire and the layout store.
        var all = graph.Edges.Select(edge => edge.Id).ToArray();
        Assert.Equal(all.Length, all.Distinct(StringComparer.Ordinal).Count());
    }

    private static HelmChart Minimal() => new(
        IsChart: true,
        new ChartMetadata("sample", "1.0.0", null, "v2", "application", string.Empty, false, 1, 3),
        MetadataFailure: null,
        Legacy: false,
        Values: [],
        Schema: null,
        Templates: [],
        Crds: null,
        Dependencies: [],
        Vendored: [],
        Lock: null);
}
