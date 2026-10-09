using EtAlii.Adp.Documents;
using EtAlii.Adp.History;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.Timeline.Tests;

/// <summary>"Arrange diagram" on a timeline: only rows change, nothing overlaps, and one undo puts it back.</summary>
public class TimelineArrangementTests : IDisposable
{
    private readonly string _workspace = IoPath.Combine(IoPath.GetTempPath(), "adp-timeline-arrange-" + Guid.NewGuid().ToString("N"));
    private readonly TimelineDocumentStore _store = new();
    private readonly HistoryStackStore _historyStacks;

    public TimelineArrangementTests()
    {
        Directory.CreateDirectory(_workspace);
        _historyStacks = new HistoryStackStore(new TimelineTestDispatcher(_store));
    }

    public void Dispose()
    {
        TestFolder.TryDelete(_workspace);
        GC.SuppressFinalize(this);
    }

    private IHistoryStack History => _historyStacks.Get(_workspace);

    /// <summary>The module's own roadmap: thirty elements scattered over seventeen rows.</summary>
    private static string Roadmap
    {
        get
        {
            for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            {
                if (directory.Name == "timeline" && Directory.Exists(IoPath.Combine(directory.FullName, "examples")))
                {
                    return File.ReadAllText(IoPath.Combine(directory.FullName, "examples", "example-2", "roadmap.tml"));
                }
            }

            throw new InvalidOperationException("No timeline folder above the test binary. That is a failure, not a skip.");
        }
    }

    private string Write(string content)
    {
        var path = IoPath.Combine(_workspace, "roadmap.tml");
        File.WriteAllText(path, content);
        _store.Forget(path);
        return path;
    }

    [Fact]
    public async Task ArrangingTheRoadmap_UsesFewerRows_OverlapsNothing_AndKeepsEveryTime()
    {
        // Arrange.
        var path = Write(Roadmap);
        var before = _store.GetOrLoad(path).Model;

        // Act.
        var result = await History.ExecuteAsync(new ArrangeTimelineCommand(path), TestContext.Current.CancellationToken);
        _store.Forget(path);
        var after = _store.GetOrLoad(path).Model;

        // Assert.
        Assert.True(result.IsSuccess, result.Error);
        Assert.True(after.Elements.Select(e => e.Row).Distinct().Count() < before.Elements.Select(e => e.Row).Distinct().Count());
        Assert.Equal(before.Elements.Select(e => (e.Id, e.Begin.Text, e.End?.Text)), after.Elements.Select(e => (e.Id, e.Begin.Text, e.End?.Text)));
        Assert.Equal(0, after.Elements.Min(e => e.Row));

        var periods = after.Elements.Where(e => e.End is not null).ToList();
        foreach (var a in periods)
        {
            foreach (var b in periods.Where(b => b.Id != a.Id && b.Row == a.Row))
            {
                Assert.True(a.End!.Value < b.Begin.Value || b.End!.Value < a.Begin.Value, $"{a.Label} and {b.Label} overlap on row {a.Row}.");
            }
        }
    }

    [Fact]
    public async Task UndoingAnArrangement_IsByteIdentical_AndRedoArrangesAgain()
    {
        // Arrange.
        var path = Write(Roadmap);
        var original = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);
        await History.ExecuteAsync(new ArrangeTimelineCommand(path), TestContext.Current.CancellationToken);
        var arranged = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);

        // Act.
        await History.UndoAsync(TestContext.Current.CancellationToken);
        var undone = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);
        await History.RedoAsync(TestContext.Current.CancellationToken);

        // Assert.
        Assert.Equal(original, undone);
        Assert.Equal(arranged, await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ArrangingTwice_IsRefusedTheSecondTime_BecauseNothingWouldMove()
    {
        // Arrange.
        var path = Write(Roadmap);
        await History.ExecuteAsync(new ArrangeTimelineCommand(path), TestContext.Current.CancellationToken);

        // Act.
        var again = await History.ExecuteAsync(new ArrangeTimelineCommand(path), TestContext.Current.CancellationToken);

        // Assert.
        Assert.False(again.IsSuccess);
        Assert.Contains("already arranged", again.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void ARelatedChain_IsArrangedOnOneRow()
    {
        // Arrange: a then b then c, related in a chain, beside an unrelated element overlapping a.
        var model = TimelineParser.Parse(LineDocument.Parse("""
            timeline: 1
            elements:
              - id: other
                label: Other
                begin: 2026-01-01
                end: 2026-03-01
                row: 0
              - id: a
                label: A
                begin: 2026-01-01
                end: 2026-03-01
                row: 5
              - id: b
                label: B
                begin: 2026-04-01
                end: 2026-06-01
                row: 0
              - id: c
                label: C
                begin: 2026-07-01
                end: 2026-09-01
                row: 9
            connections:
              - id: ab
                from: a
                to: b
              - id: bc
                from: b
                to: c
            """));

        // Act.
        var rows = TimelineArrangement.RowsOf(model);

        // Assert.
        Assert.Equal(rows["a"], rows["b"]);
        Assert.Equal(rows["b"], rows["c"]);
        Assert.NotEqual(rows["a"], rows["other"]);
    }

    [Fact]
    public void AMomentAndTheLaterElementItLeadsTo_AreNeverOnOneRow_SoTheLinkMissesItsLabel()
    {
        // Arrange: the roadmap starts with a moment, "Project kick-off", linked to the later Discovery.
        var model = TimelineParser.Parse(LineDocument.Parse(Roadmap));
        var byId = model.Elements.ToDictionary(element => element.Id, StringComparer.Ordinal);

        // Act.
        var rows = TimelineArrangement.RowsOf(model);

        // Assert: every link leaving a moment for something later runs to another row.
        var leaving = model.Connections
            .Where(connection => IsMoment(byId[connection.From]) && byId[connection.To].Begin.Value > byId[connection.From].Begin.Value)
            .ToList();
        Assert.NotEmpty(leaving);
        Assert.All(leaving, connection => Assert.NotEqual(rows[connection.From], rows[connection.To]));
        return;

        static bool IsMoment(TimelineElement element) => element.End is not { IsReadable: true };
    }
}
