using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.Databricks.Tests;

/// <summary>
/// The three computed layouts: layered DAG for the job (cycle-tolerant, stubs placed), bands
/// for the bundle, flow for the pipeline (databricks-diagrams Requirements 3-5).
/// </summary>
public class DatabricksLayoutTests
{
    private static DatabricksDocumentEntry Load(string name)
    {
        var document = DatabricksDocument.Parse(
            File.ReadAllText(IoPath.Combine(AppContext.BaseDirectory, "Fixtures", name)));
        var root = DatabricksYaml.Root(document);
        return new DatabricksDocumentEntry(
            document,
            BundleParser.Parse(root, document),
            JobParser.Parse(root, document),
            PipelineParser.Parse(root, document),
            "",
            0);
    }

    [Fact]
    public void TheJobDag_LayersByLongestPath_LeftToRight()
    {
        // Arrange.
        var job = Assert.Single(Load("job.yml").Jobs);

        // Act.
        var positions = DatabricksJobLayout.Positions(job);

        // Assert.
        // ingest -> quality_gate -> publish/alert -> refresh_dashboard: each column right of
        // its dependencies.
        Assert.True(positions["task:ingest"].X < positions["task:quality_gate"].X);
        Assert.True(positions["task:quality_gate"].X < positions["task:publish"].X);
        Assert.Equal(positions["task:publish"].X, positions["task:alert"].X);
        Assert.True(positions["task:publish"].X < positions["task:refresh_dashboard"].X);
        // The two tasks sharing a layer do not share a spot.
        Assert.NotEqual(positions["task:publish"].Y, positions["task:alert"].Y);
        // The cluster band sits beneath the DAG.
        Assert.True(positions["cluster:ingest_cluster"].Y > positions["task:alert"].Y);
    }

    [Fact]
    public void ACycle_NeitherHangsNorDropsItsMembers()
    {
        // Arrange.
        // findings-job.yml holds loop_a <-> loop_b (Requirement 4.6).
        var job = Assert.Single(Load("findings-job.yml").Jobs);

        // Act.
        var positions = DatabricksJobLayout.Positions(job);

        // Assert.
        // Both members land somewhere drawable; the back-edge just contributes nothing.
        Assert.True(positions.ContainsKey("task:loop_a"));
        Assert.True(positions.ContainsKey("task:loop_b"));
    }

    [Fact]
    public void AMissingDependsOnTarget_GetsAStubPosition()
    {
        // Arrange.
        var job = Assert.Single(Load("findings-job.yml").Jobs);

        // Act.
        var positions = DatabricksJobLayout.Positions(job);

        // Assert.
        // 'stray' depends on 'nowhere': the stub is placed so the marked-missing node and its
        // edge can draw (Requirement 4.5).
        Assert.True(positions.ContainsKey("task:nowhere"));
    }

    [Fact]
    public void TheBundle_LandsInThreeBands()
    {
        // Arrange.
        var bundle = Load("bundle.yml").Bundle;

        // Act.
        var positions = DatabricksBundleLayout.Positions(bundle);

        // Assert.
        Assert.True(positions["bundle"].Y < positions["resource:jobs/nightly_ingest"].Y);
        Assert.True(positions["resource:jobs/nightly_ingest"].Y < positions["target:dev"].Y);
        // Unknown constructs draw in the resource band rather than vanishing (Requirement 2.4).
        Assert.Equal(positions["resource:jobs/nightly_ingest"].Y, positions["unknown:sync"].Y);
        Assert.Equal(positions["target:dev"].Y, positions["target:prod"].Y);
    }

    [Fact]
    public void ThePipeline_FlowsSourcesIntoPipelineIntoTarget()
    {
        // Arrange.
        var pipeline = Assert.Single(Load("pipeline.json").Pipelines);

        // Act.
        var positions = DatabricksPipelineLayout.Positions(pipeline);

        // Assert.
        Assert.True(positions["library:transformations/bronze"].X < positions["pipeline"].X);
        Assert.True(positions["pipeline"].X < positions["target"].X);
        // The satellites sit beneath the flow.
        Assert.True(positions["compute"].Y > positions["pipeline"].Y);
        Assert.True(positions["notifications"].Y > positions["pipeline"].Y);
    }
}
