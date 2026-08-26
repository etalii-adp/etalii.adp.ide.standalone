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
        // Arrange.
        var stub = new ContextSelectionResolverStubResolver();
        var resolver = new ContextSelectionResolver([stub]);
        var selection = Level(EntryId(), "docs", "a.mm");
        selection.None = new Empty();

        var result = await resolver.ResolveChainAsync(WatchId, Root, selection, TestContext.Current.CancellationToken);

        // Act and assert, step by step.
        var resolved = Assert.IsType<ResolvedChain>(result);
        Assert.Single(resolved.Record.Levels);
        Assert.Equal(new[] { "docs", "a.mm" }, resolved.Record.Chain.Path.Segments);
        Assert.Null(resolved.Record.Action);
        Assert.Equal(ContextSelection.DetailOneofCase.None_, resolved.Record.Chain.DetailCase);
    }

    [Fact]
    public async Task ResolveChainAsync_UnsetDetail_IsTreatedAsNone()
    {
        // Arrange.
        var resolver = new ContextSelectionResolver([new ContextSelectionResolverStubResolver()]);

        var result = await resolver.ResolveChainAsync(WatchId, Root, Level(EntryId(), "a"), TestContext.Current.CancellationToken);

        // Act and assert, step by step.
        var resolved = Assert.IsType<ResolvedChain>(result);
        Assert.Equal(ContextSelection.DetailOneofCase.None_, resolved.Record.Chain.DetailCase);
    }

    [Fact]
    public async Task ResolveChainAsync_Action_IsCarriedOnTheRecord()
    {
        // Arrange.
        var resolver = new ContextSelectionResolver([new ContextSelectionResolverStubResolver()]);
        var selection = Level(EntryId(), "a");
        selection.Action = ContextSelectionAction.ContextMenu;

        var result = await resolver.ResolveChainAsync(WatchId, Root, selection, TestContext.Current.CancellationToken);

        // Act and assert, step by step.
        var resolved = Assert.IsType<ResolvedChain>(result);
        Assert.Equal(ContextSelectionAction.ContextMenu, resolved.Record.Action);
    }

    [Fact]
    public async Task ResolveChainAsync_UnknownSourceMember_IsRejected()
    {
        // Arrange.
        var resolver = new ContextSelectionResolver([new ContextSelectionResolverStubResolver(canResolve: false)]);

        // Act.
        var result = await resolver.ResolveChainAsync(WatchId, Root, Level(EntryId(), "a"), TestContext.Current.CancellationToken);

        // Assert.
        Assert.IsType<RejectedChain>(result);
    }

    [Fact]
    public async Task ResolveChainAsync_MissingId_IsRejected()
    {
        // Arrange.
        var resolver = new ContextSelectionResolver([new ContextSelectionResolverStubResolver()]);

        // Act.
        var result = await resolver.ResolveChainAsync(WatchId, Root, Level(new ContextSource(), "a"), TestContext.Current.CancellationToken);

        // Assert.
        Assert.IsType<RejectedChain>(result);
    }

    [Fact]
    public async Task ResolveChainAsync_Chain_PassesTheResolvedParentInward()
    {
        // Arrange.
        var stub = new ContextSelectionResolverStubResolver();
        var resolver = new ContextSelectionResolver([stub]);
        var parent = Level(EntryId(), "docs");
        parent.Child = Level(EntryId(), "a.mm");

        var result = await resolver.ResolveChainAsync(WatchId, Root, parent, TestContext.Current.CancellationToken);

        // Act and assert, step by step.
        var resolved = Assert.IsType<ResolvedChain>(result);
        Assert.Equal(2, resolved.Record.Levels.Count);
        Assert.Null(stub.ParentsSeen[0]);
        Assert.Same(resolved.Record.Levels[0], stub.ParentsSeen[1]);
        Assert.Same(resolved.Record.Levels[1], resolved.Record.Innermost);
    }

    [Fact]
    public async Task ResolveChainAsync_ChildUnderNotNestableParent_IsRejected()
    {
        // Arrange.
        var resolver = new ContextSelectionResolver([new ContextSelectionResolverStubResolver(nesting: ContextNesting.NotNestable)]);
        var parent = Level(EntryId(), "a.mm");
        parent.Child = Level(EntryId(), "node");

        // Act.
        var result = await resolver.ResolveChainAsync(WatchId, Root, parent, TestContext.Current.CancellationToken);

        // Assert.
        Assert.IsType<RejectedChain>(result);
    }

    [Fact]
    public async Task ResolveChainAsync_ChildTheResolverRejects_RejectsTheWholeChain()
    {
        // Arrange.
        var stub = new ContextSelectionResolverStubResolver(rejectWhenParentPresent: true);
        var resolver = new ContextSelectionResolver([stub]);
        var parent = Level(EntryId(), "docs");
        parent.Child = Level(EntryId(), "elsewhere.mm");

        // Act.
        var result = await resolver.ResolveChainAsync(WatchId, Root, parent, TestContext.Current.CancellationToken);

        // Assert.
        Assert.IsType<RejectedChain>(result);
    }

    [Fact]
    public async Task ResolveChainAsync_DeeperThanMaxDepth_IsRejected()
    {
        // Arrange.
        var resolver = new ContextSelectionResolver([new ContextSelectionResolverStubResolver()]);
        var outermost = Level(EntryId(), "l0");
        var cursor = outermost;
        for (var i = 1; i <= ContextSelectionResolver.MaxDepth; i++)
        {
            cursor.Child = Level(EntryId(), $"l{i}");
            cursor = cursor.Child;
        }

        // Act.
        var result = await resolver.ResolveChainAsync(WatchId, Root, outermost, TestContext.Current.CancellationToken);

        // Assert.
        Assert.IsType<RejectedChain>(result);
    }

    [Fact]
    public async Task ResolveChainAsync_EmptyClientPath_IsFilledInFromTheResolver()
    {
        // Arrange.
        var resolver = new ContextSelectionResolver([new ContextSelectionResolverStubResolver(fillPath: ["filled", "in.mm"])]);

        var result = await resolver.ResolveChainAsync(WatchId, Root, Level(EntryId()), TestContext.Current.CancellationToken);

        // Act and assert, step by step.
        var resolved = Assert.IsType<ResolvedChain>(result);
        Assert.Equal(new[] { "filled", "in.mm" }, resolved.Record.Chain.Path.Segments);
    }

    [Fact]
    public async Task ResolveLevelAsync_WithNoResolverForTheMember_IsRejected()
    {
        // Arrange.
        var resolver = new ContextSelectionResolver([]);

        // Act.
        var result = await resolver.ResolveLevelAsync(
            WatchId, Root, ContextSelectionSource.Ribbon, EntryId(), [], null, TestContext.Current.CancellationToken);

        // Assert.
        Assert.IsType<RejectedContextLevel>(result);
    }

    [Fact]
    public async Task ResolveLevelAsync_AsksEveryResolverThatAnswers_RatherThanStoppingAtTheFirst()
    {
        // Arrange. Several kinds of thing can share one ContextSource member - every diagram
        // type resolves an element_id - and none of them can tell from the id alone whether the
        // element is one of theirs; only the level above it says that. So the first resolver
        // asked routinely refuses something that belongs to another, and stopping there would
        // make selection work for whichever implementation happened to be registered first.
        var refuses = new ContextSelectionResolverStubResolver(alwaysReject: true);
        var accepts = new ContextSelectionResolverStubResolver();
        var resolver = new ContextSelectionResolver([refuses, accepts]);

        // Act.
        var result = await resolver.ResolveLevelAsync(
            WatchId, Root, ContextSelectionSource.Ribbon, EntryId(), [], null, TestContext.Current.CancellationToken);

        // Assert. The level carries the resolver that accepted it, which is what re-resolves and
        // tracks it later - so the wrong one answering is not a detail that stays hidden.
        var resolved = Assert.IsType<ResolvedContextLevel>(result);
        Assert.Same(accepts, resolved.Level.Resolver);
    }

    [Fact]
    public async Task ResolveLevelAsync_WhenEveryResolverRefuses_IsRejected()
    {
        // Arrange.
        var resolver = new ContextSelectionResolver(
        [
            new ContextSelectionResolverStubResolver(alwaysReject: true),
            new ContextSelectionResolverStubResolver(alwaysReject: true),
        ]);

        // Act.
        var result = await resolver.ResolveLevelAsync(
            WatchId, Root, ContextSelectionSource.Ribbon, EntryId(), [], null, TestContext.Current.CancellationToken);

        // Assert.
        Assert.IsType<RejectedContextLevel>(result);
    }

}
