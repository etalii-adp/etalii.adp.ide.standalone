using EtAlii.Adp.Backend;
using EtAlii.Adp.Backend.Context;
using EtAlii.Adp.Common;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.Databricks.Tests;

/// <summary>
/// The property grids: rows per selection kind including the workspace placeholder and the
/// simulated-run row, and every write through the standard path and the history
/// (databricks-diagrams Requirement 10).
/// </summary>
public class DatabricksContextPropertyProviderTests : IDisposable
{
    private readonly string _root;
    private readonly ServiceProvider _provider;
    private readonly DatabricksContextPropertyProvider _properties;
    private readonly IHistoryStack _history;

    public DatabricksContextPropertyProviderTests()
    {
        _root = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _provider = new ServiceCollection()
            .AddSingleton<IReadOnlyList<DiagramDefinition>>(Diagram.Definitions)
            .AddCommands()
            .AddDatabricks()
            .BuildServiceProvider();
        _properties = new DatabricksContextPropertyProvider(
            _provider.GetRequiredService<IHistoryStackStore>(),
            _provider.GetRequiredService<IDatabricksDocumentStore>());
        _history = _provider.GetRequiredService<IHistoryStackStore>().Get(_root);
    }

    public void Dispose()
    {
        _provider.Dispose();
        TestFolder.TryDelete(_root);
    }

    private string CopyFixture(string name)
    {
        var destination = IoPath.Combine(_root, name);
        File.Copy(IoPath.Combine(AppContext.BaseDirectory, "Fixtures", name), destination);
        return destination;
    }

    private ContextTarget Target(string bodyPath, string elementId) => new(
        ContextScope.DiagramElement, bodyPath, IsContainer: false, SourceId: default, _root, default, elementId);

    [Fact]
    public async Task ATask_ShowsItsGrid_WithThePlaceholderAndSimulationRows()
    {
        // Arrange.
        var body = CopyFixture("job.yml");

        // Act.
        var rows = await _properties.DescribeAsync(Target(body, "task:alert"), TestContext.Current.CancellationToken);

        // Assert.
        var byId = rows.ToDictionary(row => row.Id);
        Assert.Equal("alert", byId[DatabricksContextPropertyProvider.TaskKeyProperty].Value);
        Assert.NotEqual("", byId[DatabricksContextPropertyProvider.TaskKeyProperty].ReadOnlyReason);
        Assert.Equal("python", byId[DatabricksContextPropertyProvider.TaskTypeProperty].Value);
        Assert.Equal("AT_LEAST_ONE_FAILED", byId[DatabricksContextPropertyProvider.RunIfProperty].Value);
        // The Requirement 10.4 placeholder and the Requirement 8 simulation row, read-only with
        // their reasons.
        Assert.Equal("Not connected", byId[DatabricksContextPropertyProvider.WorkspaceProperty].Value);
        Assert.NotEqual("", byId[DatabricksContextPropertyProvider.WorkspaceProperty].ReadOnlyReason);
        Assert.NotEqual("", byId[DatabricksContextPropertyProvider.LastSimulatedRunProperty].ReadOnlyReason);
    }

    [Fact]
    public async Task SettingRunIf_GoesThroughTheHistory_AndUndoReturnsTheBytes()
    {
        // Arrange.
        var body = CopyFixture("job.yml");
        var before = await File.ReadAllBytesAsync(body, TestContext.Current.CancellationToken);

        // Act.
        var result = await _properties.SetAsync(
            Target(body, "task:publish"), DatabricksContextPropertyProvider.RunIfProperty, "ALL_DONE", TestContext.Current.CancellationToken);

        // Assert.
        Assert.True(result.IsSuccess);
        Assert.Contains("run_if: ALL_DONE", await File.ReadAllTextAsync(body, TestContext.Current.CancellationToken), StringComparison.Ordinal);
        await _history.UndoAsync(TestContext.Current.CancellationToken);
        Assert.Equal(before, await File.ReadAllBytesAsync(body, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ThePipelineNode_EditsItsScalars_InTheJsonFile()
    {
        // Arrange.
        var body = CopyFixture("pipeline.json");

        // Act.
        var rows = await _properties.DescribeAsync(Target(body, "pipeline"), TestContext.Current.CancellationToken);
        var result = await _properties.SetAsync(
            Target(body, "pipeline"), $"{DatabricksContextPropertyProvider.PipelineScalarPrefix}catalog", "lakehouse_prod", TestContext.Current.CancellationToken);

        // Assert.
        Assert.Contains(rows, row => row.Id == $"{DatabricksContextPropertyProvider.PipelineScalarPrefix}catalog");
        Assert.True(result.IsSuccess);
        Assert.Contains("\"catalog\": \"lakehouse_prod\",", await File.ReadAllTextAsync(body, TestContext.Current.CancellationToken), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AWriteToAReadOnlyRow_IsRefused()
    {
        // Arrange.
        var body = CopyFixture("job.yml");

        // Act.
        var result = await _properties.SetAsync(
            Target(body, "task:ingest"), DatabricksContextPropertyProvider.WorkspaceProperty, "https://adb.example.net", TestContext.Current.CancellationToken);

        // Assert.
        Assert.False(result.IsSuccess);
    }
}
