using Xunit;

namespace EtAlii.Adp.Diagram.Databricks.Tests;

/// <summary>
/// The bundle commands: a new resource lands beside its siblings, the bundle's name is rewritten on its one line, and an
/// edit that cannot be made is refused before anything is written (databricks-diagrams Requirements 2.2 and 6.4).
/// </summary>
public sealed class DatabricksBundleCommandsTests
{
    [Fact]
    public async Task AddResource_LandsAfterItsKindsLastSibling()
    {
        // Arrange.
        using var editing = DatabricksEditing.Of("bundle.yml");

        // Act.
        var result = await editing.RunAsync(body => new AddDatabricksResourceCommand(body, "jobs", "weekly_rollup"));

        // Assert.
        Assert.True(result.IsSuccess, result.Error);
        var model = await editing.ModelAsync();
        // The new job sits with the jobs, not at the end of the file.
        Assert.Equal(
            [("jobs", "nightly_ingest"), ("jobs", "weekly_rollup"), ("pipelines", "bronze_to_gold")],
            model.Bundle.Resources.Select(resource => (resource.Kind, resource.Key)));
        // The skeleton is complete enough to open: the job arrives with a starter task.
        Assert.Single(model.Jobs.Single(candidate => candidate.Key == "weekly_rollup").Tasks);
    }

    [Fact]
    public async Task AddResource_OnAFileWithoutResources_CreatesTheSection()
    {
        // Arrange.
        using var editing = DatabricksEditing.Holding("databricks.yml", "bundle:\r\n  name: fresh\r\n");

        // Act.
        var result = await editing.RunAsync(body => new AddDatabricksResourceCommand(body, "pipelines", "first"));

        // Assert.
        Assert.True(result.IsSuccess, result.Error);
        var pipeline = await editing.PipelineAsync();
        Assert.Equal("first", pipeline.Key);
        Assert.Single(pipeline.Libraries);
    }

    [Theory]
    [InlineData("jobs", "nightly_ingest", "already there")]
    [InlineData("experiments", "fresh", "no 'experiments' resource kind")]
    [InlineData("jobs", "", "needs a key")]
    public async Task AddResource_Refusals_HappenBeforeAnythingIsWritten(string kind, string key, string sentence)
    {
        // Arrange.
        using var editing = DatabricksEditing.Of("bundle.yml");
        var original = await editing.BytesAsync();

        // Act.
        var result = await editing.RunAsync(body => new AddDatabricksResourceCommand(body, kind, key));

        // Assert.
        Assert.Contains(sentence, result.Error, StringComparison.Ordinal);
        Assert.Equal(original, await editing.BytesAsync());
    }

    [Theory]
    [InlineData("jobs")]
    [InlineData("pipelines")]
    public async Task AddResource_OnABundleWrittenAsJson_LeavesAFileThatStillReads(string kind)
    {
        // Arrange: a bundle written as JSON (B22), which a skeleton of YAML lines would break.
        using var editing = DatabricksEditing.Of("bundle.json");
        var original = await editing.BytesAsync();

        // Act.
        var result = await editing.RunAsync(body => new AddDatabricksResourceCommand(body, kind, "added"));

        // Assert: refused with nothing written, or written so that the file still reads and holds the resource.
        if (!result.IsSuccess)
        {
            Assert.Equal(original, await editing.BytesAsync());
            return;
        }
        using var json = System.Text.Json.JsonDocument.Parse(await editing.BytesAsync());
        Assert.True(json.RootElement.GetProperty("resources").GetProperty(kind).TryGetProperty("added", out _));
    }

    [Fact]
    public async Task SetName_RewritesTheOneLine()
    {
        // Arrange.
        using var editing = DatabricksEditing.Of("bundle.yml");
        var before = (await editing.TextAsync()).Split('\n').Length;

        // Act.
        var result = await editing.RunAsync(body => new SetDatabricksBundleNameCommand(body, "lakehouse-hourly"));

        // Assert.
        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(before, (await editing.TextAsync()).Split('\n').Length);
        Assert.Equal("lakehouse-hourly", (await editing.ModelAsync()).Bundle.Name);
    }

    [Fact]
    public async Task SetName_ToNothing_IsRefused()
    {
        // Arrange.
        using var editing = DatabricksEditing.Of("bundle.yml");
        var original = await editing.BytesAsync();

        // Act.
        var result = await editing.RunAsync(body => new SetDatabricksBundleNameCommand(body, ""));

        // Assert.
        Assert.Contains("needs a name", result.Error, StringComparison.Ordinal);
        Assert.Equal(original, await editing.BytesAsync());
    }
}
