using Xunit;

namespace EtAlii.Adp.Diagram.Databricks.Tests;

/// <summary>
/// The job commands as the binding writes them: every edit's diff is minimal, a rename strands no reference, and an edit
/// that cannot be made is refused before anything is written (databricks-diagrams Requirements 2.2, 6.4 and 11.4;
/// databricks-disl-fbl Requirement 6).
/// </summary>
public sealed class DatabricksJobCommandsTests
{
    [Fact]
    public async Task ConnectThenDisconnect_OnATaskWithNoDependencies_ReturnsTheBytes()
    {
        // Arrange.
        // Connect creates the depends_on: key along with its first entry, so the disconnect that follows must remove
        // both: the round trip is exactly what an undoable command needs.
        using var editing = DatabricksEditing.Of("job.yml");
        var original = await editing.BytesAsync();

        // Act.
        var connected = await editing.RunAsync(body => new ConnectDatabricksTasksCommand(body, "", "publish", "ingest"));
        var midway = await editing.JobAsync();
        var disconnected = await editing.RunAsync(body => new DisconnectDatabricksTasksCommand(body, "", "publish", "ingest"));

        // Assert.
        Assert.True(connected.IsSuccess, connected.Error);
        Assert.Equal(["publish"], midway.Tasks[0].DependsOn.Select(dependency => dependency.TaskKey));
        Assert.True(disconnected.IsSuccess, disconnected.Error);
        Assert.Equal(original, await editing.BytesAsync());
    }

    [Fact]
    public async Task Connect_AppendsAfterTheExistingEntries_AndOnlyThere()
    {
        // Arrange.
        using var editing = DatabricksEditing.Of("job.yml");
        var before = (await editing.TextAsync()).Split('\n').Length;

        // Act.
        var result = await editing.RunAsync(body => new ConnectDatabricksTasksCommand(body, "", "alert", "refresh_dashboard"));

        // Assert.
        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(before + 1, (await editing.TextAsync()).Split('\n').Length);
        var task = (await editing.TaskAsync("refresh_dashboard"));
        Assert.Equal(["publish", "alert"], task.DependsOn.Select(dependency => dependency.TaskKey));
    }

    [Fact]
    public async Task Connect_WithAnOutcome_WritesTheOutcomeEdge()
    {
        // Arrange.
        using var editing = DatabricksEditing.Of("job.yml");

        // Act.
        var result = await editing.RunAsync(body => new ConnectDatabricksTasksCommand(body, "", "quality_gate", "refresh_dashboard", "false"));

        // Assert.
        Assert.True(result.IsSuccess, result.Error);
        var edge = (await editing.TaskAsync("refresh_dashboard")).DependsOn.Single(dependency => dependency.TaskKey == "quality_gate");
        Assert.Equal("false", edge.Outcome);
    }

    [Theory]
    [InlineData("quality_gate", "publish", "already depends")]
    [InlineData("ingest", "ingest", "cannot depend on itself")]
    [InlineData("ghost", "ingest", "not there")]
    public async Task Connect_Refusals_HappenBeforeAnythingIsWritten(string from, string to, string sentence)
    {
        // Arrange.
        using var editing = DatabricksEditing.Of("job.yml");
        var original = await editing.BytesAsync();

        // Act.
        var result = await editing.RunAsync(body => new ConnectDatabricksTasksCommand(body, "", from, to));

        // Assert.
        Assert.False(result.IsSuccess);
        Assert.Contains(sentence, result.Error, StringComparison.Ordinal);
        Assert.Equal(original, await editing.BytesAsync());
    }

    [Fact]
    public async Task RemoveTask_TakesEveryReferenceToIt_AndTheirEmptiedKeys()
    {
        // Arrange.
        // quality_gate feeds publish and alert through outcome edges; removing it removes the task's entry and both
        // references, and each consumer's emptied depends_on: key.
        using var editing = DatabricksEditing.Of("job.yml");

        // Act.
        var result = await editing.RunAsync(body => new RemoveDatabricksTaskCommand(body, "", "quality_gate"));

        // Assert.
        Assert.True(result.IsSuccess, result.Error);
        var job = await editing.JobAsync();
        Assert.Equal(4, job.Tasks.Count);
        Assert.DoesNotContain(job.Tasks, task => task.Key == "quality_gate");
        Assert.Empty(job.Tasks.Single(task => task.Key == "publish").DependsOn);
        Assert.Empty(job.Tasks.Single(task => task.Key == "alert").DependsOn);
        Assert.DoesNotContain("quality_gate", await editing.TextAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RenameTask_RewritesEveryReference_InOneEdit()
    {
        // Arrange.
        using var editing = DatabricksEditing.Of("job.yml");
        var before = (await editing.TextAsync()).Split('\n').Length;

        // Act.
        var result = await editing.RunAsync(body => new RenameDatabricksTaskCommand(body, "", "quality_gate", "gate"));

        // Assert.
        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(before, (await editing.TextAsync()).Split('\n').Length);
        var job = await editing.JobAsync();
        Assert.Contains(job.Tasks, task => task.Key == "gate");
        // No reference is stranded: both outcome edges name the new key, their outcomes intact.
        var publish = Assert.Single(job.Tasks.Single(task => task.Key == "publish").DependsOn);
        Assert.Equal(("gate", "true"), (publish.TaskKey, publish.Outcome));
        var alert = Assert.Single(job.Tasks.Single(task => task.Key == "alert").DependsOn);
        Assert.Equal(("gate", "false"), (alert.TaskKey, alert.Outcome));
        Assert.DoesNotContain("quality_gate", await editing.TextAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RenameTask_ToAnExistingKey_IsRefused()
    {
        // Arrange.
        using var editing = DatabricksEditing.Of("job.yml");
        var original = await editing.BytesAsync();

        // Act.
        var result = await editing.RunAsync(body => new RenameDatabricksTaskCommand(body, "", "ingest", "publish"));

        // Assert.
        Assert.Contains("already there", result.Error, StringComparison.Ordinal);
        Assert.Equal(original, await editing.BytesAsync());
    }

    [Fact]
    public async Task InsertTask_AppendsAfterTheLastTask_CopyingTheFileStyle()
    {
        // Arrange.
        using var editing = DatabricksEditing.Of("job.yml");
        var original = await editing.TextAsync();

        // Act.
        var result = await editing.RunAsync(body => new InsertDatabricksTaskCommand(body, "", "cleanup", "notebook", "notebooks/cleanup"));

        // Assert.
        Assert.True(result.IsSuccess, result.Error);
        // Everything before the insertion point is untouched: the new text starts with the old.
        Assert.StartsWith(original, await editing.TextAsync(), StringComparison.Ordinal);
        var job = await editing.JobAsync();
        Assert.Equal(6, job.Tasks.Count);
        Assert.Equal(("cleanup", "notebook", "notebooks/cleanup"), (job.Tasks[^1].Key, job.Tasks[^1].Type, job.Tasks[^1].Source));
    }

    [Theory]
    [InlineData("ingest", "notebook", "already there")]
    [InlineData("fresh", "shiny", "There is no 'shiny' task type to add.")]
    public async Task InsertTask_Refusals_CoverDuplicatesAndUnknownTypes(string key, string type, string sentence)
    {
        // Arrange.
        using var editing = DatabricksEditing.Of("job.yml");
        var original = await editing.BytesAsync();

        // Act.
        var result = await editing.RunAsync(body => new InsertDatabricksTaskCommand(body, "", key, type, "x"));

        // Assert.
        Assert.Contains(sentence, result.Error, StringComparison.Ordinal);
        Assert.Equal(original, await editing.BytesAsync());
    }

    [Fact]
    public async Task SetRunIf_AddsRewritesAndRemoves_TheOneLine()
    {
        // Arrange.
        // publish has no run_if: setting one adds the line, clearing it removes it again.
        using var editing = DatabricksEditing.Of("job.yml");
        var original = await editing.BytesAsync();

        // Act.
        var set = await editing.RunAsync(body => new SetDatabricksRunIfCommand(body, "", "publish", "AT_LEAST_ONE_SUCCESS"));
        var written = (await editing.TaskAsync("publish")).RunIf;
        var cleared = await editing.RunAsync(body => new SetDatabricksRunIfCommand(body, "", "publish", ""));

        // Assert.
        Assert.True(set.IsSuccess, set.Error);
        Assert.Equal("AT_LEAST_ONE_SUCCESS", written);
        Assert.True(cleared.IsSuccess, cleared.Error);
        Assert.Equal(original, await editing.BytesAsync());
    }

    [Fact]
    public async Task SetCluster_RefusesAClusterTheJobDoesNotDeclare()
    {
        // Arrange.
        using var editing = DatabricksEditing.Of("job.yml");

        // Act.
        var refused = await editing.RunAsync(body => new SetDatabricksClusterCommand(body, "", "ingest", "warp_core"));
        var bound = await editing.RunAsync(body => new SetDatabricksClusterCommand(body, "", "publish", "ingest_cluster"));

        // Assert.
        Assert.Contains("no cluster named 'warp_core'", refused.Error, StringComparison.Ordinal);
        // The declared cluster binds, on a task that had none.
        Assert.True(bound.IsSuccess, bound.Error);
        Assert.Equal("ingest_cluster", (await editing.TaskAsync("publish")).ClusterKey);
    }
}
