using Xunit;

using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Diagram.HelmChart.Tests;

/// <summary>The banded layout (Requirement 6.1): deterministic, ordered, complete.</summary>
public class HelmLayoutTests
{
    private static (HelmChart Chart, HelmGraph Graph) WellFormed()
    {
        var chart = new HelmChartReader().Read(IoPath.Combine(AppContext.BaseDirectory, "Fixtures", "well-formed"));
        return (chart, HelmGraph.Derive(chart));
    }

    [Fact]
    public void EveryNode_GetsABox()
    {
        // Arrange.
        (HelmChart chart, HelmGraph graph) = WellFormed();

        // Act.
        var boxes = HelmLayout.Compute(chart, graph);

        // Assert.
        Assert.All(graph.Nodes, node => Assert.True(boxes.ContainsKey(node.Id), $"No box for {node.Id}"));
        Assert.All(boxes.Values, box => Assert.True(box.Width > 0 && box.Height > 0));
    }

    [Fact]
    public void TheBands_RunLeftToRight()
    {
        // Arrange.
        (HelmChart chart, HelmGraph graph) = WellFormed();

        // Act.
        var boxes = HelmLayout.Compute(chart, graph);

        // Assert.
        // Metadata, values, templates, dependencies, vendored - so resolves edges run short
        // and to the right.
        Assert.True(boxes["chart"].X < boxes["values:values.yaml"].X);
        Assert.True(boxes["values:values.yaml"].X < boxes["tpl:templates/deployment.yaml"].X);
        Assert.True(boxes["tpl:templates/deployment.yaml"].X < boxes["dep:cache"].X);
        Assert.True(boxes["dep:cache"].X < boxes["sub:charts/redis"].X);
    }

    [Fact]
    public void TheDefaultLayer_TopsTheValuesStack()
    {
        // Arrange.
        (HelmChart chart, HelmGraph graph) = WellFormed();

        // Act.
        var boxes = HelmLayout.Compute(chart, graph);

        // Assert.
        Assert.True(boxes["values:values.yaml"].Y < boxes["values:values-staging.yaml"].Y);
    }

    [Fact]
    public void Manifests_ComeBeforePartialsNotesAndTests()
    {
        // Arrange.
        (HelmChart chart, HelmGraph graph) = WellFormed();

        // Act.
        var boxes = HelmLayout.Compute(chart, graph);

        // Assert.
        Assert.True(boxes["tpl:templates/deployment.yaml"].Y < boxes["tpl:templates/_helpers.tpl"].Y);
        Assert.True(boxes["tpl:templates/_helpers.tpl"].Y < boxes["tpl:templates/NOTES.txt"].Y);
        Assert.True(boxes["tpl:templates/NOTES.txt"].Y < boxes["tpl:templates/tests/test-connection.yaml"].Y);
    }

    [Fact]
    public void TheLayout_IsDeterministic()
    {
        // Arrange.
        (HelmChart chart, HelmGraph graph) = WellFormed();

        // Act.
        var first = HelmLayout.Compute(chart, graph);
        var second = HelmLayout.Compute(chart, graph);

        // Assert.
        Assert.Equal(first.Count, second.Count);
        foreach ((string id, HelmBox box) in first)
        {
            Assert.Equal(box, second[id]);
        }
    }

    [Fact]
    public void AnAbsentBand_LeavesNoHole()
    {
        // Arrange.
        // hello-world has no crds, no dependencies and nothing vendored: the values column
        // still starts right after the metadata one.
        var chart = new HelmChartReader().Read(IoPath.Combine(AppContext.BaseDirectory, "Fixtures", "broken"));
        var graph = HelmGraph.Derive(chart);

        // Act.
        var boxes = HelmLayout.Compute(chart, graph);

        // Assert.
        Assert.All(graph.Nodes, node => Assert.True(boxes.ContainsKey(node.Id)));
    }
}
