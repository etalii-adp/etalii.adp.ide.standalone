using EtAlii.Adp.Backend;
using EtAlii.Adp.Diagram;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.Timeline.Tests;

/// <summary>
/// The open diagram: baseline, deltas on change, and the two move legs - one implemented, one
/// refusing with a sentence.
/// </summary>
public class TimelineSessionTests : IDisposable
{
    private readonly string _workspace = IoPath.Combine(
        IoPath.GetTempPath(),
        "adp-timeline-session-" + Guid.NewGuid().ToString("N"));

    private readonly TimelineDocumentStore _store = new();
    private readonly TimelineElementMapper _mapper = new();
    private readonly HistoryStackStore _historyStacks;

    public TimelineSessionTests()
    {
        Directory.CreateDirectory(_workspace);
        _historyStacks = new HistoryStackStore(new TimelineTestDispatcher(_store));
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
        """;

    private string Write(string content = Timeline)
    {
        var path = IoPath.Combine(_workspace, "plan.tml");
        File.WriteAllText(path, content);
        _store.Forget(path);
        return path;
    }

    private TimelineSession Open(string path) =>
        new(path, _store, _mapper, _historyStacks.Get(_workspace));

    [Fact]
    public void TheBaselineIsOneAddCarryingEverything()
    {
        // Arrange.
        var path = Write();
        using var session = Wrap(Open(path));

        // Act.
        var baseline = session.Value.Baseline();

        // Assert.
        var add = Assert.IsType<DiagramAddDelta>(Assert.Single(baseline));
        Assert.Equal(3, add.Elements.Count);
    }

    [Fact]
    public void OpeningTheSameDocumentTwice_ProducesIdenticalOutput()
    {
        // Arrange.
        // Requirement 4.7: a diagram that shifts between openings cannot be talked about.
        var path = Write();
        using var first = Wrap(Open(path));
        using var second = Wrap(Open(path));

        // Act.
        var one = ((DiagramAddDelta)first.Value.Baseline()[0]).Elements;
        var two = ((DiagramAddDelta)second.Value.Baseline()[0]).Elements;

        // Assert.
        Assert.Equal(one.Select(element => (element.Id, element.X, element.Y, element.Type)),
            two.Select(element => (element.Id, element.X, element.Y, element.Type)));
    }

    [Fact]
    public void AChangedViewport_AddsWhatAppearedThenRemovesWhatLeft()
    {
        // Arrange: the fixture's period sits on row 0 in early 2026, the moment on row 2 a
        // fortnight later, and one connection joins them. This is the test the task calls the
        // definition of done - it fails against a UpdateView that returns [].
        var path = Write();
        using var session = Wrap(Open(path));
        session.Value.Baseline();

        var period = TimelineScale.ToSeconds(new DateTimeOffset(2026, 1, 5, 0, 0, 0, TimeSpan.Zero));
        var moment = TimelineScale.ToSeconds(new DateTimeOffset(2026, 2, 16, 14, 0, 0, TimeSpan.Zero));

        // Act: first a viewport holding only the period's row, then one holding only the moment's.
        var narrowed = session.Value.UpdateView(new DiagramViewport(period - 1, 0, period + 1, TimelineRows.Height));
        var moved = session.Value.UpdateView(new DiagramViewport(
            moment - 1, TimelineRows.ToY(2), moment + 1, TimelineRows.ToY(2) + TimelineRows.Height));

        // Assert.
        // Narrowing drops the moment and, with it, the connection whose far end has gone -
        // a curve to an element the client does not hold would have nothing to draw against.
        var dropped = Assert.IsType<DiagramRemoveDelta>(Assert.Single(narrowed));
        Assert.Equal(["bbb", "ccc"], dropped.ElementIds.Order());

        // Panning to the moment's row is one delta of each, Add first.
        Assert.Equal(2, moved.Count);
        var appeared = Assert.IsType<DiagramAddDelta>(moved[0]);
        var departed = Assert.IsType<DiagramRemoveDelta>(moved[1]);
        Assert.Equal("bbb", Assert.Single(appeared.Elements).Id);
        Assert.Equal("aaa", Assert.Single(departed.ElementIds));
    }

    [Fact]
    public void APeriodStraddlingTheViewport_StaysVisible()
    {
        // Arrange: an element is a span, not a point. The fixture's period runs 5 Jan to 13 Feb,
        // so a viewport over a day in the middle contains neither of its ends - and it is
        // precisely the bar the reader is looking at.
        var path = Write();
        using var session = Wrap(Open(path));
        session.Value.Baseline();

        var middle = TimelineScale.ToSeconds(new DateTimeOffset(2026, 1, 20, 0, 0, 0, TimeSpan.Zero));

        // Act.
        var deltas = session.Value.UpdateView(new DiagramViewport(middle, 0, middle + 60, TimelineRows.Height));

        // Assert: only the far-away moment and its connection leave; the period stays.
        var dropped = Assert.IsType<DiagramRemoveDelta>(Assert.Single(deltas));
        Assert.DoesNotContain("aaa", dropped.ElementIds);
    }

    [Fact]
    public void AConnectionSurvivesOnlyWhileBothItsEndsDo()
    {
        // Arrange: connections are packed at the origin deliberately, so they cannot be tested
        // against a viewport by position - their visibility follows their endpoints.
        var path = Write();
        using var session = Wrap(Open(path));
        session.Value.Baseline();

        // Act: a viewport wide enough in time for both, but only tall enough for row 0.
        var deltas = session.Value.UpdateView(new DiagramViewport(
            double.NegativeInfinity, 0, double.PositiveInfinity, TimelineRows.Height));

        // Assert.
        var dropped = Assert.IsType<DiagramRemoveDelta>(Assert.Single(deltas));
        Assert.Contains("ccc", dropped.ElementIds);
    }

    [Fact]
    public void AConnectionThatNeverReportsAViewport_KeepsTheWholeTimeline()
    {
        // Arrange & act: the unbounded default is what makes each module independently
        // landable - a client that has not adopted the report sees no change at all.
        var path = Write();
        using var session = Wrap(Open(path));

        // Assert.
        var add = Assert.IsType<DiagramAddDelta>(Assert.Single(session.Value.Baseline()));
        Assert.Equal(3, add.Elements.Count);
    }

    [Fact]
    public void ADocumentChangeUnderANarrowedViewport_DoesNotResendTheCulledElements()
    {
        // Arrange: the change path and the viewport path render through one place. When they did
        // not, an ordinary edit re-sent everything the viewport had just culled.
        var path = Write();
        using var session = Wrap(Open(path));
        session.Value.Baseline();
        session.Value.UpdateView(new DiagramViewport(
            double.NegativeInfinity, 0, double.PositiveInfinity, TimelineRows.Height));

        IReadOnlyList<DiagramDelta> received = [];
        session.Value.Changed += (_, args) => received = args.Deltas;

        // Act: retitle the period, which is inside the viewport.
        File.WriteAllText(path, Timeline.Replace("label: Period", "label: Renamed", StringComparison.Ordinal));
        _store.Reload(path);

        // Assert: the edit arrives, and nothing outside the viewport comes back with it.
        var add = Assert.IsType<DiagramAddDelta>(received[0]);
        Assert.Equal("aaa", Assert.Single(add.Elements).Id);
    }

    [Fact]
    public async Task TheReparentingMove_IsRefusedWithAReason()
    {
        // Arrange.
        var path = Write();
        await using var session = Open(path);

        // Act.
        var answer = await session.MoveElementAsync("aaa", "bbb", 0, CancellationToken.None);

        // Assert.
        Assert.NotEmpty(answer);
        Assert.Contains("no parent", answer, StringComparison.Ordinal);
    }

    [Fact]
    public async Task APositionalMove_RewritesTheElementAndPreservesItsDuration()
    {
        // Arrange.
        var path = Write();
        await using var session = Open(path);
        session.Baseline();
        var landing = TimelineScale.ToSeconds(new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero));

        // Act.
        var answer = await session.MoveElementToAsync("aaa", landing, TimelineRows.ToY(4), CancellationToken.None);

        // Assert.
        Assert.Equal("", answer);
        var text = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);
        Assert.Contains("begin: 2026-03-01", text, StringComparison.Ordinal);
        Assert.Contains("end: 2026-04-09", text, StringComparison.Ordinal); // 39 days later, as before
        Assert.Contains("row: 4", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AMove_IsOneUndoAway()
    {
        // Arrange.
        var path = Write();
        var before = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);
        await using var session = Open(path);
        var landing = TimelineScale.ToSeconds(new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero));

        // Act.
        await session.MoveElementToAsync("aaa", landing, TimelineRows.ToY(4), CancellationToken.None);
        await _historyStacks.Get(_workspace).UndoAsync(CancellationToken.None);

        // Assert.
        Assert.Equal(before, await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task MovingAConnection_IsRefused()
    {
        // Arrange.
        // A curve has no position of its own - it follows its endpoints.
        var path = Write();
        await using var session = Open(path);

        // Act.
        var answer = await session.MoveElementToAsync("ccc", 0, 0, CancellationToken.None);

        // Assert.
        Assert.NotEmpty(answer);
    }

    [Fact]
    public async Task AReadOnlySession_RefusesTheMove()
    {
        // Arrange.
        var path = Write();
        await using var session = new TimelineSession(path, _store, _mapper, history: null);

        // Act.
        var answer = await session.MoveElementToAsync("aaa", 0, 0, CancellationToken.None);

        // Assert.
        Assert.Equal("This timeline is read-only.", answer);
    }

    [Fact]
    public async Task AChangeFromAnywhere_ReachesTheSessionAsADiff()
    {
        // Arrange.
        // An edit is an add carrying the new state; this session never dispatched anything, so
        // what it receives is purely the store's announcement.
        var path = Write();
        await using var session = Open(path);
        session.Baseline();
        IReadOnlyList<DiagramDelta>? received = null;
        session.Changed += (_, args) => received = args.Deltas;

        // Act.
        await File.WriteAllTextAsync(path, Timeline.Replace("label: Period", "label: Renamed", StringComparison.Ordinal), TestContext.Current.CancellationToken);
        _store.Reload(path);

        // Assert.
        Assert.NotNull(received);
        var add = Assert.IsType<DiagramAddDelta>(Assert.Single(received));
        Assert.Equal("aaa", Assert.Single(add.Elements).Id);
    }

    [Fact]
    public async Task AnUnrelatedDocumentsChange_DoesNotDisturbThisSession()
    {
        // Arrange.
        var path = Write();
        var otherPath = IoPath.Combine(_workspace, "other.tml");
        await File.WriteAllTextAsync(otherPath, Timeline, TestContext.Current.CancellationToken);
        await using var session = Open(path);
        session.Baseline();
        var disturbed = false;
        session.Changed += (_, _) => disturbed = true;

        // Act.
        _store.Reload(otherPath);

        // Assert.
        Assert.False(disturbed);
    }

    /// <summary>Bridges IAsyncDisposable into a using for the tests that never await.</summary>
    private static Disposer Wrap(TimelineSession session) => new(session);

    private sealed class Disposer(TimelineSession session) : IDisposable
    {
        public TimelineSession Value { get; } = session;

        public void Dispose() => Value.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}
