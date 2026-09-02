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
        var text = File.ReadAllText(path);
        Assert.Contains("x: 812.5", text, StringComparison.Ordinal);
        Assert.Contains("row: 4", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AMove_IsOneUndoAway()
    {
        // Arrange.
        var path = Write();
        var before = File.ReadAllText(path);
        await using var session = Open(path);

        // Act.
        await session.MoveElementToAsync("aaa", 812.5, DependencyGraphRows.ToY(4), CancellationToken.None);
        await _historyStacks.Get(_workspace).UndoAsync(CancellationToken.None);

        // Assert.
        Assert.Equal(before, File.ReadAllText(path));
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
        File.WriteAllText(path, Graph.Replace("label: API gateway", "label: Renamed", StringComparison.Ordinal));
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
        File.WriteAllText(path, Graph.Replace("x: 240", "x: 999", StringComparison.Ordinal));
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
        File.WriteAllText(otherPath, Graph);
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
