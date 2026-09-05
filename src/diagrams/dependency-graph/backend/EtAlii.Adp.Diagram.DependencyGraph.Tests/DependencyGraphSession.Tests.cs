using EtAlii.Adp.Backend;
using EtAlii.Adp.Backend.Diagrams;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.DependencyGraph.Tests;

/// <summary>
/// The open diagram: baseline, deltas on change, and the two move legs - one implemented, one
/// refusing with a sentence.
/// </summary>
public class DependencyGraphSessionTests : IDisposable
{
    private readonly string _workspace = IoPath.Combine(
        IoPath.GetTempPath(),
        "adp-dgr-session-" + Guid.NewGuid().ToString("N"));

    private readonly DependencyGraphDocumentStore _store = new();
    private readonly DependencyGraphElementMapper _mapper = new();
    private readonly HistoryStackStore _historyStacks;

    public DependencyGraphSessionTests()
    {
        Directory.CreateDirectory(_workspace);
        _historyStacks = new HistoryStackStore(new DependencyGraphTestDispatcher(_store));
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
            x: 480
            row: 2
        relations:
          - id: ccc
            from: aaa
            to: bbb
        """;

    private string Write(string content = Graph)
    {
        var path = IoPath.Combine(_workspace, "services.dgr");
        File.WriteAllText(path, content);
        _store.Forget(path);
        return path;
    }

    private DependencyGraphSession Open(string path) =>
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
        // A diagram that shifts between openings cannot be talked about.
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

    /// <summary>
    /// The behavioural half of the view-delta loop: a changed viewport produces deltas, in the
    /// Add-then-Remove order both reference implementations use. This is the test that fails
    /// against the `return []` this session answered with before adoption - asserting that the
    /// client called `UpdateView` would not, because an empty answer passes that too
    /// (view-delta-adoption Requirement 1.3).
    /// </summary>
    /// <remarks>
    /// The fixture places `aaa` at x 240 row 0 and `bbb` at x 480 row 2, so with a node drawn
    /// 160 wide and 36 tall their boxes are 240..400 by 0..36 and 480..640 by 120..156 - far
    /// enough apart that one viewport can hold either alone.
    /// </remarks>
    [Fact]
    public void AChangedViewport_AddsWhatAppeared_ThenRemovesWhatLeft()
    {
        // Arrange: opened whole, then narrowed to the first node alone.
        var path = Write();
        using var session = Wrap(Open(path));
        session.Value.Baseline();
        session.Value.UpdateView(new DiagramViewport(200, -10, 420, 60));

        // Act: move the view to the second node, which the first no longer touches.
        var deltas = session.Value.UpdateView(new DiagramViewport(450, 100, 700, 200));

        // Assert: Add first, then Remove - the order the design corrected Requirement 4.3 to.
        Assert.Equal(2, deltas.Count);
        var appeared = Assert.IsType<DiagramAddDelta>(deltas[0]);
        var departed = Assert.IsType<DiagramRemoveDelta>(deltas[1]);
        Assert.Equal(["bbb"], appeared.Elements.Select(element => element.Id));
        Assert.Equal(["aaa"], departed.ElementIds);
    }

    /// <summary>
    /// A relation has no box of its own: it is delivered exactly when both of its ends are, and
    /// withdrawn as soon as either leaves - which is also what the canvas does with a curve
    /// whose endpoint it does not hold.
    /// </summary>
    [Fact]
    public void ARelation_TravelsWithBothOfItsEnds()
    {
        // Arrange: one node in view, so the relation is not.
        var path = Write();
        using var session = Wrap(Open(path));
        session.Value.Baseline();
        session.Value.UpdateView(new DiagramViewport(200, -10, 420, 60));

        // Act: widen to hold both nodes.
        var widened = session.Value.UpdateView(new DiagramViewport(0, -10, 900, 300));

        // Assert: the second node and the relation arrive together, and nothing leaves.
        var appeared = Assert.IsType<DiagramAddDelta>(widened[0]);
        Assert.Equal(["bbb", "ccc"], appeared.Elements.Select(element => element.Id).Order());
        Assert.DoesNotContain(widened, delta => delta is DiagramRemoveDelta);

        // Act, continued: narrow back to the first node alone.
        var narrowed = session.Value.UpdateView(new DiagramViewport(200, -10, 420, 60));

        // Assert: the relation leaves with the end that left.
        var departed = Assert.IsType<DiagramRemoveDelta>(narrowed.Single());
        Assert.Equal(["bbb", "ccc"], departed.ElementIds.Order());
    }

    /// <summary>
    /// A connection that never reports a viewport keeps the whole graph it opened with: the
    /// session starts unbounded, so adoption changes nothing for a client that does not report.
    /// </summary>
    [Fact]
    public void WithoutAReportedViewport_TheBaselineIsTheWholeGraph()
    {
        // Arrange & act.
        var path = Write();
        using var session = Wrap(Open(path));
        var baseline = Assert.IsType<DiagramAddDelta>(session.Value.Baseline().Single());

        // Assert.
        Assert.Equal(["aaa", "bbb", "ccc"], baseline.Elements.Select(element => element.Id).Order());
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
    public async Task APositionalMove_RewritesTheNodesCoordinateAndRow()
    {
        // Arrange.
        var path = Write();
        await using var session = Open(path);
        session.Baseline();

        // Act.
        var answer = await session.MoveElementToAsync("aaa", 812.5, DependencyGraphRows.ToY(4), CancellationToken.None);

        // Assert.
        Assert.Equal("", answer);
        var text = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);
        Assert.Contains("x: 812.5", text, StringComparison.Ordinal);
        Assert.Contains("row: 4", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AMove_IsOneUndoAway()
    {
        // Arrange.
        var path = Write();
        var before = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);
        await using var session = Open(path);

        // Act.
        await session.MoveElementToAsync("aaa", 812.5, DependencyGraphRows.ToY(4), CancellationToken.None);
        await _historyStacks.Get(_workspace).UndoAsync(CancellationToken.None);

        // Assert.
        Assert.Equal(before, await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task MovingARelation_IsRefused()
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
        await using var session = new DependencyGraphSession(path, _store, _mapper, history: null);

        // Act.
        var answer = await session.MoveElementToAsync("aaa", 0, 0, CancellationToken.None);

        // Assert.
        Assert.Equal("This graph is read-only.", answer);
    }

    /// <summary>
    /// The two paths agree about what this connection holds. The viewport path culls, and the
    /// change path has to render through the same decision - otherwise an ordinary edit re-sends
    /// every element the viewport just removed, and the client silently regains what it was told
    /// to drop. Invisible until someone edits while zoomed in, which is why it is asserted here
    /// as an interaction rather than left to the two methods' own tests.
    /// </summary>
    [Fact]
    public async Task ADocumentChangeUnderANarrowedViewport_DoesNotResendTheCulledElements()
    {
        // Arrange: narrowed to the first node alone, so the second and the relation are culled.
        var path = Write();
        await using var session = Open(path);
        session.Baseline();
        session.UpdateView(new DiagramViewport(200, -10, 420, 60));

        IReadOnlyList<DiagramDelta>? received = null;
        session.Changed += (_, args) => received = args.Deltas;

        // Act: rename the node that is inside the viewport.
        await File.WriteAllTextAsync(path, Graph.Replace("label: API gateway", "label: Renamed", StringComparison.Ordinal), TestContext.Current.CancellationToken);
        _store.Reload(path);

        // Assert: the edit arrives, and nothing the viewport had culled comes back with it.
        Assert.NotNull(received);
        var add = Assert.IsType<DiagramAddDelta>(Assert.Single(received));
        Assert.Equal("aaa", Assert.Single(add.Elements).Id);
    }

    [Fact]
    public async Task AChangeFromAnywhere_ReachesTheSessionAsADiff()
    {
        // Arrange.
        // An external edit reaching an open graph is what the reloader's bridge exists for; this
        // session never dispatched anything, so what it receives is purely the store's
        // announcement.
        var path = Write();
        await using var session = Open(path);
        session.Baseline();
        IReadOnlyList<DiagramDelta>? received = null;
        session.Changed += (_, args) => received = args.Deltas;

        // Act.
        await File.WriteAllTextAsync(path, Graph.Replace("label: API gateway", "label: Renamed", StringComparison.Ordinal), TestContext.Current.CancellationToken);
        _store.Reload(path);

        // Assert.
        Assert.NotNull(received);
        var add = Assert.IsType<DiagramAddDelta>(Assert.Single(received));
        Assert.Equal("aaa", Assert.Single(add.Elements).Id);
    }

    [Fact]
    public async Task TheReloader_IsWhatTheWatcherBridgeCalls()
    {
        // Arrange.
        // The seam core reaches, rather than the store directly: a reloader wired to the wrong
        // origin is an external edit that never arrives, with nothing failing anywhere.
        var path = Write();
        var reloader = new DependencyGraphDocumentReloader(_store);
        await using var session = Open(path);
        session.Baseline();
        var disturbed = false;
        session.Changed += (_, _) => disturbed = true;

        // Act.
        await File.WriteAllTextAsync(path, Graph.Replace("x: 240", "x: 999", StringComparison.Ordinal), TestContext.Current.CancellationToken);
        reloader.Reload(_workspace, path);

        // Assert.
        Assert.Equal(Diagram.DependencyGraph.Origin, reloader.Origin);
        Assert.True(disturbed);
        Assert.Equal(999d, _store.GetOrLoad(path).Model.Elements.Single(element => element.Id == "aaa").X);
    }

    [Fact]
    public async Task AnUnrelatedDocumentsChange_DoesNotDisturbThisSession()
    {
        // Arrange.
        var path = Write();
        var otherPath = IoPath.Combine(_workspace, "other.dgr");
        await File.WriteAllTextAsync(otherPath, Graph, TestContext.Current.CancellationToken);
        await using var session = Open(path);
        session.Baseline();
        var disturbed = false;
        session.Changed += (_, _) => disturbed = true;

        // Act.
        _store.Reload(otherPath);

        // Assert.
        Assert.False(disturbed);
    }

    [Fact]
    public void TheFactoryOpensASessionForThisOrigin()
    {
        // Arrange.
        var path = Write();
        var factory = new DependencyGraphSessionFactory(_store, _mapper, _historyStacks);

        // Act.
        var session = factory.Open(ShortGuid.NewShortGuid(), _workspace, path, registrationPath: null);

        // Assert.
        // A bare .dgr routes with no registration at all, so the factory must not need one.
        Assert.Equal(Diagram.DependencyGraph.Origin, factory.Origin);
        using var wrapped = Wrap(Assert.IsType<DependencyGraphSession>(session));
        Assert.NotEmpty(wrapped.Value.Baseline());
    }

    /// <summary>Bridges IAsyncDisposable into a using for the tests that never await.</summary>
    private static Disposer Wrap(DependencyGraphSession session) => new(session);

    private sealed class Disposer(DependencyGraphSession session) : IDisposable
    {
        public DependencyGraphSession Value { get; } = session;

        public void Dispose() => Value.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}
