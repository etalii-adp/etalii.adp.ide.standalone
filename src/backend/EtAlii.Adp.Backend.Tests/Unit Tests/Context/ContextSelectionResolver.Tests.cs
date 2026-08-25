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
        var stub = new ContextSelectionResolverStubResolver();
        var resolver = new ContextSelectionResolver([stub]);
        var selection = Level(EntryId(), "docs", "a.mm");
        selection.None = new Empty();

        var result = await resolver.ResolveChainAsync(WatchId, Root, selection, TestContext.Current.CancellationToken);

        var resolved = Assert.IsType<ResolvedChain>(result);
        Assert.Single(resolved.Record.Levels);
        Assert.Equal(new[] { "docs", "a.mm" }, resolved.Record.Chain.Path.Segments);
        Assert.Null(resolved.Record.Action);
        Assert.Equal(ContextSelection.DetailOneofCase.None_, resolved.Record.Chain.DetailCase);
    }

    [Fact]
    public async Task ResolveChainAsync_UnsetDetail_IsTreatedAsNone()
    {
        var resolver = new ContextSelectionResolver([new ContextSelectionResolverStubResolver()]);

        var result = await resolver.ResolveChainAsync(WatchId, Root, Level(EntryId(), "a"), TestContext.Current.CancellationToken);

        var resolved = Assert.IsType<ResolvedChain>(result);
        Assert.Equal(ContextSelection.DetailOneofCase.None_, resolved.Record.Chain.DetailCase);
    }

    [Fact]
    public async Task ResolveChainAsync_Action_IsCarriedOnTheRecord()
    {
        var resolver = new ContextSelectionResolver([new ContextSelectionResolverStubResolver()]);
        var selection = Level(EntryId(), "a");
        selection.Action = ContextSelectionAction.ContextMenu;

        var result = await resolver.ResolveChainAsync(WatchId, Root, selection, TestContext.Current.CancellationToken);

        var resolved = Assert.IsType<ResolvedChain>(result);
        Assert.Equal(ContextSelectionAction.ContextMenu, resolved.Record.Action);
    }

    [Fact]
    public async Task ResolveChainAsync_UnknownSourceMember_IsRejected()
    {
        var resolver = new ContextSelectionResolver([new ContextSelectionResolverStubResolver(canResolve: false)]);

        var result = await resolver.ResolveChainAsync(WatchId, Root, Level(EntryId(), "a"), TestContext.Current.CancellationToken);

        Assert.IsType<RejectedChain>(result);
    }

    [Fact]
    public async Task ResolveChainAsync_MissingId_IsRejected()
    {
        var resolver = new ContextSelectionResolver([new ContextSelectionResolverStubResolver()]);

        var result = await resolver.ResolveChainAsync(WatchId, Root, Level(new ContextSource(), "a"), TestContext.Current.CancellationToken);

        Assert.IsType<RejectedChain>(result);
    }

    [Fact]
    public async Task ResolveChainAsync_Chain_PassesTheResolvedParentInward()
    {
        var stub = new ContextSelectionResolverStubResolver();
        var resolver = new ContextSelectionResolver([stub]);
        var parent = Level(EntryId(), "docs");
        parent.Child = Level(EntryId(), "a.mm");

        var result = await resolver.ResolveChainAsync(WatchId, Root, parent, TestContext.Current.CancellationToken);

        var resolved = Assert.IsType<ResolvedChain>(result);
        Assert.Equal(2, resolved.Record.Levels.Count);
        Assert.Null(stub.ParentsSeen[0]);
        Assert.Same(resolved.Record.Levels[0], stub.ParentsSeen[1]);
        Assert.Same(resolved.Record.Levels[1], resolved.Record.Innermost);
    }

    [Fact]
    public async Task ResolveChainAsync_ChildUnderNotNestableParent_IsRejected()
    {
        var resolver = new ContextSelectionResolver([new ContextSelectionResolverStubResolver(nesting: ContextNesting.NotNestable)]);
        var parent = Level(EntryId(), "a.mm");
        parent.Child = Level(EntryId(), "node");

        var result = await resolver.ResolveChainAsync(WatchId, Root, parent, TestContext.Current.CancellationToken);

        Assert.IsType<RejectedChain>(result);
    }

    [Fact]
    public async Task ResolveChainAsync_ChildTheResolverRejects_RejectsTheWholeChain()
    {
        var stub = new ContextSelectionResolverStubResolver(rejectWhenParentPresent: true);
        var resolver = new ContextSelectionResolver([stub]);
        var parent = Level(EntryId(), "docs");
        parent.Child = Level(EntryId(), "elsewhere.mm");

        var result = await resolver.ResolveChainAsync(WatchId, Root, parent, TestContext.Current.CancellationToken);

        Assert.IsType<RejectedChain>(result);
    }

    [Fact]
    public async Task ResolveChainAsync_DeeperThanMaxDepth_IsRejected()
    {
        var resolver = new ContextSelectionResolver([new ContextSelectionResolverStubResolver()]);
        var outermost = Level(EntryId(), "l0");
        var cursor = outermost;
        for (var i = 1; i <= ContextSelectionResolver.MaxDepth; i++)
        {
            cursor.Child = Level(EntryId(), $"l{i}");
            cursor = cursor.Child;
        }

        var result = await resolver.ResolveChainAsync(WatchId, Root, outermost, TestContext.Current.CancellationToken);

        Assert.IsType<RejectedChain>(result);
    }

    [Fact]
    public async Task ResolveChainAsync_EmptyClientPath_IsFilledInFromTheResolver()
    {
        var resolver = new ContextSelectionResolver([new ContextSelectionResolverStubResolver(fillPath: ["filled", "in.mm"])]);

        var result = await resolver.ResolveChainAsync(WatchId, Root, Level(EntryId()), TestContext.Current.CancellationToken);

        var resolved = Assert.IsType<ResolvedChain>(result);
        Assert.Equal(new[] { "filled", "in.mm" }, resolved.Record.Chain.Path.Segments);
    }

    [Fact]
    public async Task ResolveLevelAsync_WithNoResolverForTheMember_IsRejected()
    {
        var resolver = new ContextSelectionResolver([]);

        var result = await resolver.ResolveLevelAsync(
            WatchId, Root, ContextSelectionSource.Ribbon, EntryId(), [], null, TestContext.Current.CancellationToken);

        Assert.IsType<RejectedContextLevel>(result);
    }

}
