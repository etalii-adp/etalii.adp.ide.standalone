using EtAlii.Adp.Layout;
using EtAlii.Adp.Specification.Disl;
using Xunit;

namespace EtAlii.Adp.Diagram.HelmChart.Tests;

/// <summary>The banded layout (Requirement 6.1): deterministic, ordered, complete.</summary>
public class HelmLayoutTests
{
    private static DislDiagram WellFormed() => HelmFolder.Reading(HelmFolder.Fixture("well-formed")).Disl.Diagram;

    [Fact]
    public void EveryNode_GetsABox()
    {
        // Arrange.
        var diagram = WellFormed();

        // Act.
        var boxes = HelmLayout.Compute(diagram);

        // Assert.
        Assert.All(diagram.Nodes, node => Assert.True(boxes.ContainsKey(node.Id), $"No box for {node.Id}"));
        Assert.All(boxes.Values, box => Assert.True(box is { Width: > 0, Height: > 0 }));
    }

    [Fact]
    public void TheBands_RunLeftToRight()
    {
        // Arrange.
        var diagram = WellFormed();

        // Act.
        var boxes = HelmLayout.Compute(diagram);

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
        var diagram = WellFormed();

        // Act.
        var boxes = HelmLayout.Compute(diagram);

        // Assert.
        Assert.True(boxes["values:values.yaml"].Y < boxes["values:values-staging.yaml"].Y);
    }

    [Fact]
    public void Manifests_ComeBeforePartialsNotesAndTests()
    {
        // Arrange.
        var diagram = WellFormed();

        // Act.
        var boxes = HelmLayout.Compute(diagram);

        // Assert.
        Assert.True(boxes["tpl:templates/deployment.yaml"].Y < boxes["tpl:templates/_helpers.tpl"].Y);
        Assert.True(boxes["tpl:templates/_helpers.tpl"].Y < boxes["tpl:templates/NOTES.txt"].Y);
        Assert.True(boxes["tpl:templates/NOTES.txt"].Y < boxes["tpl:templates/tests/test-connection.yaml"].Y);
    }

    [Fact]
    public void TheLayout_IsDeterministic()
    {
        // Arrange.
        var diagram = WellFormed();

        // Act.
        var first = HelmLayout.Compute(diagram);
        var second = HelmLayout.Compute(diagram);

        // Assert.
        Assert.Equal(first.Count, second.Count);
        foreach ((string id, Rect box) in first)
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
        var diagram = HelmFolder.Reading(HelmFolder.Fixture("broken")).Disl.Diagram;

        // Act.
        var boxes = HelmLayout.Compute(diagram);

        // Assert.
        Assert.All(diagram.Nodes, node => Assert.True(boxes.ContainsKey(node.Id)));
    }
}
