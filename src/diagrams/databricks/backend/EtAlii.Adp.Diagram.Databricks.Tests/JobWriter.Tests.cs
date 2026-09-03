using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.Databricks.Tests;

/// <summary>
/// The job splices: every operation's diff is minimal, rename strands no reference, and invalid
/// writes are refused before any splice (databricks-diagrams Requirements 2.2, 6.4 and 11.4).
/// </summary>
public class JobWriterTests
{
    private static DatabricksDocument Load()
    {
        return DatabricksDocument.Parse(
            File.ReadAllText(IoPath.Combine(AppContext.BaseDirectory, "Fixtures", "job.yml")));
    }

    private static JobModel Job(DatabricksDocument document) =>
        Assert.Single(JobParser.Parse(DatabricksYaml.Root(document), document));

    [Fact]
    public void ConnectThenDisconnect_OnATaskWithNoDependencies_ReturnsTheBytes()
    {
        // Arrange.
        // Connect creates the depends_on: key along with its first entry, so its inverse must
        // remove both - the round trip is exactly what an undoable command needs.
        var document = Load();
        var original = document.Text;

        // Act.
        var connected = JobWriter.Connect(document, Job(document), "publish", "ingest");
        var midway = Job(document);
        var disconnected = JobWriter.Disconnect(document, midway, "publish", "ingest");

        // Assert.
        Assert.Equal("", connected);
        Assert.Equal(new[] { "publish" }, midway.Tasks[0].DependsOn.Select(dependency => dependency.TaskKey));
        Assert.Equal("", disconnected);
        Assert.Equal(original, document.Text);
    }

    [Fact]
    public void Connect_AppendsAfterTheExistingEntries_AndOnlyThere()
    {
        // Arrange.
        var document = Load();
        var before = document.Lines.Count;

        // Act.
        var refusal = JobWriter.Connect(document, Job(document), "alert", "refresh_dashboard");

        // Assert.
        Assert.Equal("", refusal);
        Assert.Equal(before + 1, document.Lines.Count);
        var task = Job(document).Tasks.Single(candidate => candidate.Key == "refresh_dashboard");
        Assert.Equal(
            new[] { "publish", "alert" },
            task.DependsOn.Select(dependency => dependency.TaskKey));
    }

    [Fact]
    public void Connect_WithAnOutcome_WritesTheOutcomeEdge()
    {
        // Arrange.
        var document = Load();

        // Act.
        var refusal = JobWriter.Connect(document, Job(document), "quality_gate", "refresh_dashboard", "false");

        // Assert.
        Assert.Equal("", refusal);
        var task = Job(document).Tasks.Single(candidate => candidate.Key == "refresh_dashboard");
        var edge = task.DependsOn.Single(dependency => dependency.TaskKey == "quality_gate");
        Assert.Equal("false", edge.Outcome);
    }

    [Fact]
    public void Connect_Refusals_HappenBeforeAnySplice()
    {
        // Arrange.
        var document = Load();
        var original = document.Text;

        // Act & assert.
        Assert.Contains("already depends", JobWriter.Connect(document, Job(document), "quality_gate", "publish"), StringComparison.Ordinal);
        Assert.Contains("cannot depend on itself", JobWriter.Connect(document, Job(document), "ingest", "ingest"), StringComparison.Ordinal);
        Assert.Contains("not there", JobWriter.Connect(document, Job(document), "ghost", "ingest"), StringComparison.Ordinal);
        Assert.Equal(original, document.Text);
    }

    [Fact]
    public void RemoveTask_TakesEveryReferenceToIt_AndTheirEmptiedKeys()
    {
        // Arrange.
        // quality_gate feeds publish and alert through outcome edges; removing it must remove
        // the task block and both references - and each consumer's now-empty depends_on: key.
        var document = Load();
        var job = Job(document);
        Assert.Equal(2, JobWriter.ReferencesTo(job, "quality_gate").Count);

        // Act.
        var refusal = JobWriter.RemoveTask(document, job, "quality_gate");

        // Assert.
        Assert.Equal("", refusal);
        var reparsed = Job(document);
        Assert.Equal(4, reparsed.Tasks.Count);
        Assert.DoesNotContain(reparsed.Tasks, task => task.Key == "quality_gate");
        Assert.Empty(reparsed.Tasks.Single(task => task.Key == "publish").DependsOn);
        Assert.Empty(reparsed.Tasks.Single(task => task.Key == "alert").DependsOn);
        Assert.DoesNotContain("quality_gate", document.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void RenameTask_RewritesEveryReference_InOneOperation()
    {
        // Arrange.
        var document = Load();
        var before = document.Lines.Count;

        // Act.
        var refusal = JobWriter.RenameTask(document, Job(document), "quality_gate", "gate");

        // Assert.
        Assert.Equal("", refusal);
        Assert.Equal(before, document.Lines.Count);
        var reparsed = Job(document);
        Assert.Contains(reparsed.Tasks, task => task.Key == "gate");
        // No reference is stranded: both outcome edges now name the new key, outcomes intact.
        var publish = Assert.Single(reparsed.Tasks.Single(task => task.Key == "publish").DependsOn);
        Assert.Equal(("gate", "true"), (publish.TaskKey, publish.Outcome));
        var alert = Assert.Single(reparsed.Tasks.Single(task => task.Key == "alert").DependsOn);
        Assert.Equal(("gate", "false"), (alert.TaskKey, alert.Outcome));
        Assert.DoesNotContain("quality_gate", document.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void RenameTask_ToAnExistingKey_IsRefused()
    {
        // Arrange.
        var document = Load();
        var original = document.Text;

        // Act & assert.
        Assert.Contains("already there", JobWriter.RenameTask(document, Job(document), "ingest", "publish"), StringComparison.Ordinal);
        Assert.Equal(original, document.Text);
    }

    [Fact]
    public void InsertTask_AppendsAfterTheLastTask_CopyingTheFileStyle()
    {
        // Arrange.
        var document = Load();
        var original = document.Text;

        // Act.
        var refusal = JobWriter.InsertTask(document, Job(document), "cleanup", "notebook", "notebooks/cleanup");

        // Assert.
        Assert.Equal("", refusal);
        // Everything before the insertion point is untouched: the new text starts with the old.
        Assert.StartsWith(original, document.Text, StringComparison.Ordinal);
        var reparsed = Job(document);
        Assert.Equal(6, reparsed.Tasks.Count);
        Assert.Equal(("cleanup", "notebook", "notebooks/cleanup"), (reparsed.Tasks[^1].Key, reparsed.Tasks[^1].Type, reparsed.Tasks[^1].Source));
    }

    [Fact]
    public void InsertTask_Refusals_CoverDuplicatesAndUnknownTypes()
    {
        // Arrange.
        var document = Load();
        var original = document.Text;

        // Act & assert.
        Assert.Contains("already there", JobWriter.InsertTask(document, Job(document), "ingest", "notebook", "x"), StringComparison.Ordinal);
        Assert.Contains("no 'shiny' task type", JobWriter.InsertTask(document, Job(document), "fresh", "shiny", "x"), StringComparison.Ordinal);
        Assert.Equal(original, document.Text);
    }

    [Fact]
    public void SetRunIf_AddsRewritesAndRemoves_TheOneLine()
    {
        // Arrange.
        var document = Load();
        var original = document.Text;

        // Act & assert.
        // publish has no run_if: setting one adds the line, clearing it removes it again.
        Assert.Equal("", JobWriter.SetRunIf(document, Job(document), "publish", "AT_LEAST_ONE_SUCCESS"));
        Assert.Equal("AT_LEAST_ONE_SUCCESS", Job(document).Tasks.Single(task => task.Key == "publish").RunIf);
        Assert.Equal("", JobWriter.SetRunIf(document, Job(document), "publish", ""));
        Assert.Equal(original, document.Text);
    }

    [Fact]
    public void SetCluster_RefusesAClusterTheJobDoesNotDeclare()
    {
        // Arrange.
        var document = Load();

        // Act & assert.
        Assert.Contains("no cluster named 'warp_core'", JobWriter.SetCluster(document, Job(document), "ingest", "warp_core"), StringComparison.Ordinal);
        // The declared cluster binds fine, on a task that had none.
        Assert.Equal("", JobWriter.SetCluster(document, Job(document), "publish", "ingest_cluster"));
        Assert.Equal("ingest_cluster", Job(document).Tasks.Single(task => task.Key == "publish").ClusterKey);
    }
}
