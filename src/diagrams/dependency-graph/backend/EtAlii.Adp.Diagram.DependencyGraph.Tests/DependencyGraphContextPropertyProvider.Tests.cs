using EtAlii.Adp.Common.Wire;
using EtAlii.Adp.Context;
using EtAlii.Adp.History;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.DependencyGraph.Tests;

/// <summary>
/// The property rows, and above all what is <b>not</b> among them: begin, end and duration are
/// absent rather than blanked, which is the requirement this file exists to hold.
/// </summary>
public class DependencyGraphContextPropertyProviderTests : IDisposable
{
    private readonly string _workspace = IoPath.Combine(
        IoPath.GetTempPath(),
        "adp-dgr-properties-" + Guid.NewGuid().ToString("N"));

    private readonly DependencyGraphDocumentStore _store = new();
    private readonly DependencyGraphContextPropertyProvider _properties;
    private readonly HistoryStackStore _historyStacks;

    public DependencyGraphContextPropertyProviderTests()
    {
        Directory.CreateDirectory(_workspace);
        _historyStacks = new HistoryStackStore(new DependencyGraphTestDispatcher(_store));
        _properties = new DependencyGraphContextPropertyProvider(_historyStacks, _store);
    }

    public void Dispose()
    {
        TestFolder.TryDelete(_workspace);

        GC.SuppressFinalize(this);
    }

    private const string Graph = """
        dependencies: 1
        elements:
          - id: aaa
            label: API gateway
            x: 240
            row: 0
          - id: bbb
            label: Identity service
            x: 480.5
            row: 2
        relations:
          - id: ccc
            from: aaa
            to: bbb
            label: verifies tokens with
        """;

    private string Write()
    {
        var path = IoPath.Combine(_workspace, "services.dgr");
        File.WriteAllText(path, Graph);
        _store.Forget(path);
        return path;
    }

    private ContextTarget Target(string path, string elementId) =>
        new(ContextScope.DiagramElement, path, IsContainer: false, SourceId: default, _workspace, ShortGuid.NewShortGuid(), elementId);

    [Fact]
    public async Task ANode_ContributesLabelXAndRow_AndNothingElse()
    {
        // Arrange & act.
        var path = Write();
        var rows = await _properties.DescribeAsync(Target(path, "aaa"), CancellationToken.None);

        // Assert.
        Assert.Equal(
            [
                DependencyGraphContextPropertyProvider.LabelProperty,
                DependencyGraphContextPropertyProvider.XProperty,
                DependencyGraphContextPropertyProvider.RowProperty,
            ],
            rows.Select(row => row.Id));
        Assert.All(rows, row => Assert.True(row.IsEditable));
    }

    [Fact]
    public async Task NoTimeDerivedRowExists_AbsentRatherThanBlank()
    {
        // Arrange & act.
        // Absent, not empty: a blank "Begin" field would invite a value this type has nowhere to
        // put, and the grid is where a fork most easily leaves one behind.
        var path = Write();
        var rows = await _properties.DescribeAsync(Target(path, "aaa"), CancellationToken.None);

        // Assert.
        foreach (var label in new[] { "Begin", "End", "Duration" })
        {
            Assert.DoesNotContain(label, rows.Select(row => row.Label), StringComparer.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task ANodesCoordinate_IsShownAsTheDocumentWroteIt()
    {
        // Arrange & act.
        // A whole 240 shown as "240" and a fractional one as "480.5": the grid shows the authored
        // number, not a rendering of it.
        var path = Write();
        var whole = await _properties.DescribeAsync(Target(path, "aaa"), CancellationToken.None);
        var fractional = await _properties.DescribeAsync(Target(path, "bbb"), CancellationToken.None);

        // Assert.
        Assert.Equal("240", whole.Single(row => row.Id == DependencyGraphContextPropertyProvider.XProperty).Value);
        Assert.Equal("480.5", fractional.Single(row => row.Id == DependencyGraphContextPropertyProvider.XProperty).Value);
    }

    [Fact]
    public async Task ADependency_ShowsItsEndsReadOnly_WithTheReason()
    {
        // Arrange & act.
        var path = Write();
        var rows = await _properties.DescribeAsync(Target(path, "ccc"), CancellationToken.None);

        // Assert.
        var from = rows.Single(row => row.Id == DependencyGraphContextPropertyProvider.FromProperty);
        var to = rows.Single(row => row.Id == DependencyGraphContextPropertyProvider.ToProperty);
        Assert.False(from.IsEditable);
        Assert.False(to.IsEditable);
        Assert.Equal("aaa", from.Value);
        Assert.Equal("bbb", to.Value);
        Assert.Contains("canvas", from.ReadOnlyReason, StringComparison.Ordinal);
        Assert.True(rows.Single(row => row.Id == DependencyGraphContextPropertyProvider.LabelProperty).IsEditable);
    }

    [Fact]
    public async Task AnEditTravelsAsACommand_AndIsOneUndoAway()
    {
        // Arrange.
        var path = Write();
        var before = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);

        // Act.
        var result = await _properties.SetAsync(
            Target(path, "aaa"), DependencyGraphContextPropertyProvider.XProperty, "612", CancellationToken.None);
        await _historyStacks.Get(_workspace).UndoAsync(CancellationToken.None);

        // Assert.
        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(before, await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AFractionalCoordinate_IsAccepted()
    {
        // Arrange & act.
        var path = Write();
        var result = await _properties.SetAsync(
            Target(path, "aaa"), DependencyGraphContextPropertyProvider.XProperty, "612.75", CancellationToken.None);

        // Assert.
        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(612.75d, _store.GetOrLoad(path).Model.Elements[0].X);
    }

    [Fact]
    public async Task ACoordinateThatIsNotANumber_IsRefusedNamingTheValue()
    {
        // Arrange & act.
        var path = Write();
        var result = await _properties.SetAsync(
            Target(path, "aaa"), DependencyGraphContextPropertyProvider.XProperty, "sideways", CancellationToken.None);

        // Assert.
        Assert.False(result.IsSuccess);
        Assert.Contains("sideways", result.Error, StringComparison.Ordinal);
        Assert.Contains("x: 240", await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AFractionalRow_IsRefusedRatherThanTruncated()
    {
        // Arrange & act.
        // A row is an index into a placement grid; silently rounding 2.6 to 3 would put the node
        // somewhere the user did not ask for and never say so.
        var path = Write();
        var result = await _properties.SetAsync(
            Target(path, "aaa"), DependencyGraphContextPropertyProvider.RowProperty, "2.6", CancellationToken.None);

        // Assert.
        Assert.False(result.IsSuccess);
        Assert.Equal(0, _store.GetOrLoad(path).Model.Elements[0].Row);
    }

    [Fact]
    public async Task EditingTheRow_MovesTheNode()
    {
        // Arrange & act.
        var path = Write();
        var result = await _properties.SetAsync(
            Target(path, "aaa"), DependencyGraphContextPropertyProvider.RowProperty, "7", CancellationToken.None);

        // Assert.
        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(7, _store.GetOrLoad(path).Model.Elements[0].Row);
    }

    [Fact]
    public async Task EditingALabel_Renames()
    {
        // Arrange & act.
        var path = Write();
        var result = await _properties.SetAsync(
            Target(path, "aaa"), DependencyGraphContextPropertyProvider.LabelProperty, "Edge router", CancellationToken.None);

        // Assert.
        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal("Edge router", _store.GetOrLoad(path).Model.Elements[0].Label);
    }

    [Fact]
    public async Task ADependencysLabel_IsEditable_ButItsEndsAreNot()
    {
        // Arrange.
        var path = Write();

        // Act.
        var relabelled = await _properties.SetAsync(
            Target(path, "ccc"), DependencyGraphContextPropertyProvider.LabelProperty, "authenticates against", CancellationToken.None);
        var reversed = await _properties.SetAsync(
            Target(path, "ccc"), DependencyGraphContextPropertyProvider.FromProperty, "bbb", CancellationToken.None);

        // Assert.
        // The read-only rows are read-only through the seam as well as in the description: a
        // grid edit that reversed a dependency would be the quietest way to say the opposite
        // thing.
        Assert.True(relabelled.IsSuccess, relabelled.Error);
        Assert.False(reversed.IsSuccess);
        var relation = _store.GetOrLoad(path).Model.Relations[0];
        Assert.Equal("authenticates against", relation.Label);
        Assert.Equal("aaa", relation.From);
    }

    [Fact]
    public async Task AnUnknownProperty_IsRefused()
    {
        // Arrange & act.
        var path = Write();
        var result = await _properties.SetAsync(Target(path, "aaa"), "dependencies.nonsense", "x", CancellationToken.None);

        // Assert.
        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task AnotherTypesFile_GetsNoRows()
    {
        // Arrange.
        var foreign = IoPath.Combine(_workspace, "plan.tml");
        await File.WriteAllTextAsync(foreign, "timeline: 1\nelements: []\n", TestContext.Current.CancellationToken);

        // Act.
        var rows = await _properties.DescribeAsync(Target(foreign, "x"), CancellationToken.None);

        // Assert.
        Assert.Empty(rows);
    }
}
