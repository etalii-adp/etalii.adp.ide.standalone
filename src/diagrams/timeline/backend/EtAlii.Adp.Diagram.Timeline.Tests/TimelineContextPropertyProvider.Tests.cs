using EtAlii.Adp.Context;
using EtAlii.Adp.Documents.Wire;
using EtAlii.Adp.History;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.Timeline.Tests;

/// <summary>
/// The property rows, and above all their refusals - the grid is the second of the two paths
/// Requirement 3.4 closes, and every guard here is tested independently of the canvas.
/// </summary>
public class TimelineContextPropertyProviderTests : IDisposable
{
    private readonly string _workspace = IoPath.Combine(
        IoPath.GetTempPath(),
        "adp-timeline-properties-" + Guid.NewGuid().ToString("N"));

    private readonly TimelineDocumentStore _store = new();
    private readonly TimelineContextPropertyProvider _properties;
    private readonly HistoryStackStore _historyStacks;

    public TimelineContextPropertyProviderTests()
    {
        Directory.CreateDirectory(_workspace);
        _historyStacks = new HistoryStackStore(new TimelineTestDispatcher(_store));
        _properties = new TimelineContextPropertyProvider(_historyStacks, _store);
    }

    public void Dispose()
    {
        TestFolder.TryDelete(_workspace);

        GC.SuppressFinalize(this);
    }

    private const string Timeline = """
        timeline: 1
        elements:
          - id: aaa
            label: Period
            begin: 2026-01-05
            end: 2026-02-13
            row: 0
          - id: bbb
            label: Moment
            begin: 2026-02-16T14:00:00
            row: 2
        connections:
          - id: ccc
            from: aaa
            to: bbb
            label: gates
        """;

    private string Write()
    {
        var path = IoPath.Combine(_workspace, "plan.tml");
        File.WriteAllText(path, Timeline);
        _store.Forget(path);
        return path;
    }

    private ContextTarget Target(string path, string elementId) =>
        new(ContextScope.DiagramElement, path, IsContainer: false, SourceId: default, _workspace, ShortGuid.NewShortGuid(), elementId);

    [Fact]
    public async Task APeriod_ContributesLabelBeginEndAndRow()
    {
        // Arrange & act.
        var path = Write();
        var rows = await _properties.DescribeAsync(Target(path, "aaa"), CancellationToken.None);

        // Assert.
        Assert.Equal(
            [
                TimelineContextPropertyProvider.LabelProperty,
                TimelineContextPropertyProvider.BeginProperty,
                TimelineContextPropertyProvider.EndProperty,
                TimelineContextPropertyProvider.RowProperty,
            ],
            rows.Select(row => row.Id));
        Assert.All(rows, row => Assert.True(row.IsEditable));
    }

    [Fact]
    public async Task AMoment_ContributesNoEndRowAtAll()
    {
        // Arrange & act.
        // Absent, not empty: the action that gives a moment an end is the menu's, and a blank
        // field inviting a value would be a second, unguarded path to the same edit.
        var path = Write();
        var rows = await _properties.DescribeAsync(Target(path, "bbb"), CancellationToken.None);

        // Assert.
        Assert.DoesNotContain(TimelineContextPropertyProvider.EndProperty, rows.Select(row => row.Id));
    }

    [Fact]
    public async Task AConnection_ShowsItsEndpointsReadOnly_WithTheReason()
    {
        // Arrange & act.
        var path = Write();
        var rows = await _properties.DescribeAsync(Target(path, "ccc"), CancellationToken.None);

        // Assert.
        var from = rows.Single(row => row.Id == TimelineContextPropertyProvider.FromProperty);
        var to = rows.Single(row => row.Id == TimelineContextPropertyProvider.ToProperty);
        Assert.False(from.IsEditable);
        Assert.False(to.IsEditable);
        Assert.Contains("canvas", from.ReadOnlyReason, StringComparison.Ordinal);
        Assert.True(rows.Single(row => row.Id == TimelineContextPropertyProvider.LabelProperty).IsEditable);
    }

    [Fact]
    public async Task AnEditTravelsAsACommand_AndIsOneUndoAway()
    {
        // Arrange.
        var path = Write();
        var before = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);

        // Act.
        var result = await _properties.SetAsync(Target(path, "aaa"), TimelineContextPropertyProvider.BeginProperty, "2026-01-10", CancellationToken.None);
        await _historyStacks.Get(_workspace).UndoAsync(CancellationToken.None);

        // Assert.
        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(before, await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AValueThatWouldInvert_IsRefusedWithAReason()
    {
        // Arrange & act.
        // The grid path of Requirement 3.4, closed on the same terms the adorner's handler
        // applies - and proven here without any canvas in sight.
        var path = Write();
        var result = await _properties.SetAsync(Target(path, "aaa"), TimelineContextPropertyProvider.BeginProperty, "2026-03-01", CancellationToken.None);

        // Assert.
        Assert.False(result.IsSuccess);
        Assert.Contains("cannot end before it begins", result.Error, StringComparison.Ordinal);
        Assert.Contains("begin: 2026-01-05", await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AMalformedTime_IsRefusedNamingTheValue()
    {
        // Arrange & act.
        var path = Write();
        var result = await _properties.SetAsync(Target(path, "aaa"), TimelineContextPropertyProvider.BeginProperty, "sideways", CancellationToken.None);

        // Assert.
        Assert.False(result.IsSuccess);
        Assert.Contains("sideways", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MixingPrecisionWithinOneElement_IsRefused()
    {
        // Arrange & act.
        // Requirement 3.2: the grid is the only path that can change one of the pair
        // independently, so the guard lives here.
        var path = Write();
        var result = await _properties.SetAsync(Target(path, "aaa"), TimelineContextPropertyProvider.BeginProperty, "2026-01-05T09:00:00", CancellationToken.None);

        // Assert.
        Assert.False(result.IsSuccess);
        Assert.Contains("both", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EditingTheRow_MovesTheElement()
    {
        // Arrange & act.
        var path = Write();
        var result = await _properties.SetAsync(Target(path, "aaa"), TimelineContextPropertyProvider.RowProperty, "7", CancellationToken.None);

        // Assert.
        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(7, _store.GetOrLoad(path).Model.Elements[0].Row);
    }

    [Fact]
    public async Task AnUnknownProperty_IsRefused()
    {
        // Arrange & act.
        var path = Write();
        var result = await _properties.SetAsync(Target(path, "aaa"), "timeline.nonsense", "x", CancellationToken.None);

        // Assert.
        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task AnotherTypesFile_GetsNoRows()
    {
        // Arrange.
        var foreign = IoPath.Combine(_workspace, "map.owm");
        await File.WriteAllTextAsync(foreign, "title something\n", TestContext.Current.CancellationToken);

        // Act.
        var rows = await _properties.DescribeAsync(Target(foreign, "x"), CancellationToken.None);

        // Assert.
        Assert.Empty(rows);
    }
}
