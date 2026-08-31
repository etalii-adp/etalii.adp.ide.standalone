using EtAlii.Adp.Backend;
using EtAlii.Adp.Backend.Diagrams;
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
        if (Directory.Exists(_workspace))
        {
            Directory.Delete(_workspace, recursive: true);
        }

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
    public void UpdateView_SendsNothingNew()
    {
        // Arrange.
        var path = Write();
        using var session = Wrap(Open(path));
        session.Value.Baseline();

        // Act & assert.
        Assert.Empty(session.Value.UpdateView(new DiagramViewport(0, 0, 1000, 1000)));
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
        var text = File.ReadAllText(path);
        Assert.Contains("begin: 2026-03-01", text, StringComparison.Ordinal);
        Assert.Contains("end: 2026-04-09", text, StringComparison.Ordinal); // 39 days later, as before
        Assert.Contains("row: 4", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AMove_IsOneUndoAway()
    {
        // Arrange.
        var path = Write();
        var before = File.ReadAllText(path);
        await using var session = Open(path);
        var landing = TimelineScale.ToSeconds(new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero));

        // Act.
        await session.MoveElementToAsync("aaa", landing, TimelineRows.ToY(4), CancellationToken.None);
        await _historyStacks.Get(_workspace).UndoAsync(CancellationToken.None);

        // Assert.
        Assert.Equal(before, File.ReadAllText(path));
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
        File.WriteAllText(path, Timeline.Replace("label: Period", "label: Renamed", StringComparison.Ordinal));
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
        File.WriteAllText(otherPath, Timeline);
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
