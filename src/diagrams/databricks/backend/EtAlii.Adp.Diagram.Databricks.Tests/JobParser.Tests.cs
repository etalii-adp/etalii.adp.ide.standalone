using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.Databricks.Tests;

/// <summary>
/// The job resource reading: the task DAG with types, outcome edges, run_if and cluster
/// bindings (databricks-diagrams Requirement 4).
/// </summary>
public class JobParserTests
{
    private static IReadOnlyList<JobModel> Parse(string name)
    {
        var document = DatabricksDocument.Parse(
            File.ReadAllText(IoPath.Combine(AppContext.BaseDirectory, "Fixtures", name)));
        return JobParser.Parse(DatabricksYaml.Root(document), document);
    }

    [Fact]
    public void TheJobFixture_ReadsItsIdentityScheduleAndCluster()
    {
        // Arrange & act.
        var job = Assert.Single(Parse("job.yml"));

        // Assert.
        Assert.Equal("nightly_ingest", job.Key);
        Assert.Equal("Nightly ingest", job.Name);
        Assert.Equal("0 0 1 * * ?", job.Schedule);
        Assert.False(job.Continuous);
        var cluster = Assert.Single(job.Clusters);
        Assert.Equal("ingest_cluster", cluster.Key);
        Assert.Equal("15.4.x-scala2.12", cluster.SparkVersion);
        Assert.Equal("Standard_DS3_v2", cluster.NodeType);
        Assert.Equal(2, cluster.Workers);
    }

    [Fact]
    public void Tasks_CarryTheirTypesAndSources()
    {
        // Arrange & act.
        var job = Assert.Single(Parse("job.yml"));

        // Assert.
        Assert.Equal(5, job.Tasks.Count);
        Assert.Equal(
            new[] { "ingest", "quality_gate", "publish", "alert", "refresh_dashboard" },
            job.Tasks.Select(task => task.Key));
        Assert.Equal(
            new[] { "notebook", "condition", "notebook", "python", "sql" },
            job.Tasks.Select(task => task.Type));
        Assert.Equal("notebooks/ingest", job.Tasks[0].Source);
        Assert.Equal("scripts/alert.py", job.Tasks[3].Source);
        Assert.Equal("9d3f2b1a", job.Tasks[4].Source);
    }

    [Fact]
    public void Dependencies_CarryOutcomes_AndRunIfIsReadWhereWritten()
    {
        // Arrange & act.
        var job = Assert.Single(Parse("job.yml"));

        // Assert.
        // The condition task's two consumers each follow one outcome edge (Requirement 4.3).
        var publish = Assert.Single(job.Tasks[2].DependsOn);
        Assert.Equal(("quality_gate", "true"), (publish.TaskKey, publish.Outcome));
        var alert = Assert.Single(job.Tasks[3].DependsOn);
        Assert.Equal(("quality_gate", "false"), (alert.TaskKey, alert.Outcome));
        Assert.Equal("AT_LEAST_ONE_FAILED", job.Tasks[3].RunIf);
        // An unwritten run_if is empty - the default ALL_SUCCESS is the payload's to say.
        Assert.Equal("", job.Tasks[2].RunIf);
    }

    [Fact]
    public void ClusterBinding_IsPerTask_AndEmptyMeansServerless()
    {
        // Arrange & act.
        var job = Assert.Single(Parse("job.yml"));

        // Assert.
        Assert.Equal("ingest_cluster", job.Tasks[0].ClusterKey);
        Assert.Equal("", job.Tasks[1].ClusterKey);
    }

    [Fact]
    public void EachTask_KnowsItsOwnLines_AndTheyDoNotOverlap()
    {
        // Arrange & act.
        var job = Assert.Single(Parse("job.yml"));

        // Assert.
        // The ranges are what the writers splice: a task bleeding into its neighbour's lines
        // would make every removal rewrite an unrelated task.
        for (var i = 1; i < job.Tasks.Count; i++)
        {
            Assert.True(
                job.Tasks[i - 1].Lines.End < job.Tasks[i].Lines.Start,
                $"{job.Tasks[i - 1].Key} ends at {job.Tasks[i - 1].Lines.End}, "
                + $"{job.Tasks[i].Key} starts at {job.Tasks[i].Lines.Start}");
        }
    }

    [Fact]
    public void AFileWithoutJobs_ReadsAsNone()
    {
        // Arrange & act & assert.
        Assert.Empty(Parse("pipeline.json"));
    }
}
