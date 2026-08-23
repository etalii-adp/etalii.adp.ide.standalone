using EtAlii.Adp.Backend.Context;

using Google.Protobuf.WellKnownTypes;
using Xunit;

namespace EtAlii.Adp.Backend.Tests;

/// <summary>
/// The chain resolver must know nothing about any concrete kind of thing, so every test
/// here uses stub resolvers; naming the hierarchy's would prove nothing about that.
/// </summary>
public class ContextSelectionResolverTests
{
    private static readonly ShortGuid WatchId = ShortGuid.NewShortGuid();
    private const string Root = @"C:\root";

    private static ContextSelection Level(ContextSource id, params string[] path)
    {
        var selection = new ContextSelection { Source = ContextSelectionSource.Explorer, Id = id, Path = new Path() };
        selection.Path.Segments.AddRange(path);
        return selection;
    }

    private static ContextSource EntryId() => new() { EntryId = ShortGuid.NewShortGuid() };

    [Fact]
    public async Task ResolveChainAsync_SingleLevel_ResolvesThroughTheMatchingResolver()
    {
        var stub = new StubResolver();
        var resolver = new ContextSelectionResolver([stub]);
        var selection = Level(EntryId(), "docs", "a.mm");
        selection.None = new Empty();

        var result = await resolver.ResolveChainAsync(WatchId, Root, selection, TestContext.Current.CancellationToken);

        var resolved = Assert.IsType<ChainResolution.Resolved>(result);
        Assert.Single(resolved.Record.Levels);
        Assert.Equal(new[] { "docs", "a.mm" }, resolved.Record.Chain.Path.Segments);
        Assert.Null(resolved.Record.Action);
        Assert.Equal(ContextSelection.DetailOneofCase.None_, resolved.Record.Chain.DetailCase);
    }

    [Fact]
    public async Task ResolveChainAsync_UnsetDetail_IsTreatedAsNone()
    {
        var resolver = new ContextSelectionResolver([new StubResolver()]);

        var result = await resolver.ResolveChainAsync(WatchId, Root, Level(EntryId(), "a"), TestContext.Current.CancellationToken);

        var resolved = Assert.IsType<ChainResolution.Resolved>(result);
        Assert.Equal(ContextSelection.DetailOneofCase.None_, resolved.Record.Chain.DetailCase);
    }

    [Fact]
    public async Task ResolveChainAsync_Action_IsCarriedOnTheRecord()
    {
        var resolver = new ContextSelectionResolver([new StubResolver()]);
        var selection = Level(EntryId(), "a");
        selection.Action = ContextSelectionAction.ContextMenu;

        var result = await resolver.ResolveChainAsync(WatchId, Root, selection, TestContext.Current.CancellationToken);

        var resolved = Assert.IsType<ChainResolution.Resolved>(result);
        Assert.Equal(ContextSelectionAction.ContextMenu, resolved.Record.Action);
    }

    [Fact]
    public async Task ResolveChainAsync_UnknownSourceMember_IsRejected()
    {
        var resolver = new ContextSelectionResolver([new StubResolver(canResolve: false)]);

        var result = await resolver.ResolveChainAsync(WatchId, Root, Level(EntryId(), "a"), TestContext.Current.CancellationToken);

        Assert.IsType<ChainResolution.Rejected>(result);
    }

    [Fact]
    public async Task ResolveChainAsync_MissingId_IsRejected()
    {
        var resolver = new ContextSelectionResolver([new StubResolver()]);

        var result = await resolver.ResolveChainAsync(WatchId, Root, Level(new ContextSource(), "a"), TestContext.Current.CancellationToken);

        Assert.IsType<ChainResolution.Rejected>(result);
    }

    [Fact]
    public async Task ResolveChainAsync_Chain_PassesTheResolvedParentInward()
    {
        var stub = new StubResolver();
        var resolver = new ContextSelectionResolver([stub]);
        var parent = Level(EntryId(), "docs");
        parent.Child = Level(EntryId(), "a.mm");

        var result = await resolver.ResolveChainAsync(WatchId, Root, parent, TestContext.Current.CancellationToken);

        var resolved = Assert.IsType<ChainResolution.Resolved>(result);
        Assert.Equal(2, resolved.Record.Levels.Count);
        Assert.Null(stub.ParentsSeen[0]);
        Assert.Same(resolved.Record.Levels[0], stub.ParentsSeen[1]);
        Assert.Same(resolved.Record.Levels[1], resolved.Record.Innermost);
    }

    [Fact]
    public async Task ResolveChainAsync_ChildUnderNotNestableParent_IsRejected()
    {
        var resolver = new ContextSelectionResolver([new StubResolver(nesting: ContextNesting.NotNestable)]);
        var parent = Level(EntryId(), "a.mm");
        parent.Child = Level(EntryId(), "node");

        var result = await resolver.ResolveChainAsync(WatchId, Root, parent, TestContext.Current.CancellationToken);

        Assert.IsType<ChainResolution.Rejected>(result);
    }

    [Fact]
    public async Task ResolveChainAsync_ChildTheResolverRejects_RejectsTheWholeChain()
    {
        var stub = new StubResolver(rejectWhenParentPresent: true);
        var resolver = new ContextSelectionResolver([stub]);
        var parent = Level(EntryId(), "docs");
        parent.Child = Level(EntryId(), "elsewhere.mm");

        var result = await resolver.ResolveChainAsync(WatchId, Root, parent, TestContext.Current.CancellationToken);

        Assert.IsType<ChainResolution.Rejected>(result);
    }

    [Fact]
    public async Task ResolveChainAsync_DeeperThanMaxDepth_IsRejected()
    {
        var resolver = new ContextSelectionResolver([new StubResolver()]);
        var outermost = Level(EntryId(), "l0");
        var cursor = outermost;
        for (var i = 1; i <= ContextSelectionResolver.MaxDepth; i++)
        {
            cursor.Child = Level(EntryId(), $"l{i}");
            cursor = cursor.Child;
        }

        var result = await resolver.ResolveChainAsync(WatchId, Root, outermost, TestContext.Current.CancellationToken);

        Assert.IsType<ChainResolution.Rejected>(result);
    }

    [Fact]
    public async Task ResolveChainAsync_EmptyClientPath_IsFilledInFromTheResolver()
    {
        var resolver = new ContextSelectionResolver([new StubResolver(fillPath: ["filled", "in.mm"])]);

        var result = await resolver.ResolveChainAsync(WatchId, Root, Level(EntryId()), TestContext.Current.CancellationToken);

        var resolved = Assert.IsType<ChainResolution.Resolved>(result);
        Assert.Equal(new[] { "filled", "in.mm" }, resolved.Record.Chain.Path.Segments);
    }

    [Fact]
    public async Task ResolveLevelAsync_WithNoResolverForTheMember_IsRejected()
    {
        var resolver = new ContextSelectionResolver([]);

        var result = await resolver.ResolveLevelAsync(
            WatchId, Root, ContextSelectionSource.Ribbon, EntryId(), [], null, TestContext.Current.CancellationToken);

        Assert.IsType<ContextLevelResolution.Rejected>(result);
    }

    /// <summary>
    /// Answers for entry ids, echoing the client path (or a fixed fill-in), and records
    /// which parent it was handed at each level so the chain walk can be asserted.
    /// </summary>
    private sealed class StubResolver : IContextSourceResolver
    {
        private readonly bool _canResolve;
        private readonly ContextNesting _nesting;
        private readonly bool _rejectWhenParentPresent;
        private readonly IReadOnlyList<string>? _fillPath;

        public StubResolver(
            bool canResolve = true,
            ContextNesting nesting = ContextNesting.Contained,
            bool rejectWhenParentPresent = false,
            IReadOnlyList<string>? fillPath = null)
        {
            _canResolve = canResolve;
            _nesting = nesting;
            _rejectWhenParentPresent = rejectWhenParentPresent;
            _fillPath = fillPath;
        }

        public List<ContextResolvedLevel?> ParentsSeen { get; } = new();

        public bool CanResolve(ContextSource source) => _canResolve && source.SourceCase == ContextSource.SourceOneofCase.EntryId;

        public ValueTask<ContextLevelResolution> ResolveAsync(
            ShortGuid watchId, string rootPath, ContextSelectionSource source, ContextSource id,
            IReadOnlyList<string> clientPath, ContextResolvedLevel? parent, CancellationToken cancellationToken)
        {
            ParentsSeen.Add(parent);
            if (_rejectWhenParentPresent && parent is not null)
            {
                return ValueTask.FromResult<ContextLevelResolution>(new ContextLevelResolution.Rejected("not inside its parent"));
            }

            var path = clientPath.Count == 0 && _fillPath is not null ? _fillPath : clientPath;
            var level = new ContextResolvedLevel(
                source, id, path, ContextScope.Hierarchy,
                new ContextTarget(ContextScope.Hierarchy, System.IO.Path.Combine(rootPath, string.Join('\\', path)), false, (ShortGuid)id.EntryId),
                new ContextLevelDetail(), this);
            return ValueTask.FromResult<ContextLevelResolution>(new ContextLevelResolution.Resolved(level));
        }

        public ContextNesting NestingOf(ContextResolvedLevel level) => _nesting;

        public IDisposable Track(ShortGuid watchId, string rootPath, ContextResolvedLevel level, Action<IReadOnlyList<string>?> onChange) => new NoopDisposable();

        private sealed class NoopDisposable : IDisposable
        {
            public void Dispose()
            {
            }
        }
    }
}
