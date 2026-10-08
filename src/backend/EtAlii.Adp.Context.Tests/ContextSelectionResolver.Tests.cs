using EtAlii.Adp.Context.Wire;
using EtAlii.Adp.Documents.Wire;
using Google.Protobuf.WellKnownTypes;
using Xunit;
using Path = EtAlii.Adp.Documents.Wire.Path;
namespace EtAlii.Adp.Context.Tests;

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
            WatchId, Root, EntryId(), [], null, TestContext.Current.CancellationToken);

        // Assert.
        Assert.IsType<RejectedContextLevel>(result);
    }

    [Fact]
    public async Task ResolveLevelAsync_WhenSeveralResolversClaimTheIdShape_TakesTheOneThatOwnsIt()
    {
        // Arrange.
        // Every diagram type's resolver claims every element id, because CanResolve answers a
        // question about the shape of an id and not about ownership - which element belongs to
        // which type depends on the file the enclosing level names, and only ResolveAsync looks
        // at that.
        //
        // Taking the first claimant therefore gave whichever module registered earliest silent
        // ownership of every element selection in the application. A C4 element could not be
        // selected at all: the mindmap resolver claimed it and then correctly observed that the
        // file was not a mindmap. Nothing caught it, because every test that selected an element
        // selected a mindmap one.
        var first = new ContextSelectionResolverRefusingStubResolver("mindmap-node", "not a mindmap");
        var second = new ContextSelectionResolverRefusingStubResolver("pipeline-stage", "not a pipeline");
        var resolver = new ContextSelectionResolver([first, second]);

        // Act.
        var result = await resolver.ResolveLevelAsync(
            WatchId,
            Root,
            new ContextSource { ElementId = new ElementId { Value = "pipeline-stage" } },
            [],
            null,
            TestContext.Current.CancellationToken);

        // Assert.
        var resolved = Assert.IsType<ResolvedContextLevel>(result);
        Assert.Equal("pipeline-stage", resolved.Level.Target.ElementId);
        Assert.Equal(1, first.Asked);
        Assert.Equal(1, second.Asked);
    }

    [Fact]
    public async Task ResolveLevelAsync_StopsAtTheFirstResolverThatAccepts()
    {
        // Arrange: asking the rest would be wasted work, and a second acceptance would be a
        // disagreement nobody could adjudicate.
        var first = new ContextSelectionResolverRefusingStubResolver("shared-id", "not mine");
        var second = new ContextSelectionResolverRefusingStubResolver("shared-id", "not mine either");
        var resolver = new ContextSelectionResolver([first, second]);

        // Act.
        await resolver.ResolveLevelAsync(
            WatchId,
            Root,
            new ContextSource { ElementId = new ElementId { Value = "shared-id" } },
            [],
            null,
            TestContext.Current.CancellationToken);

        // Assert.
        Assert.Equal(1, first.Asked);
        Assert.Equal(0, second.Asked);
    }

    [Fact]
    public async Task ResolveLevelAsync_WhenEveryClaimantRefuses_SaysNoWithoutSayingWhichOne()
    {
        // Arrange: asking several resolvers must not become a way to find out which module
        // recognised the file. Every rejection carries the same generic reason, and offering the
        // level around does not weaken that.
        var first = new ContextSelectionResolverRefusingStubResolver("mindmap-node", "not a mindmap");
        var second = new ContextSelectionResolverRefusingStubResolver("pipeline-stage", "not a pipeline");
        var resolver = new ContextSelectionResolver([first, second]);

        // Act.
        var result = await resolver.ResolveLevelAsync(
            WatchId,
            Root,
            new ContextSource { ElementId = new ElementId { Value = "belongs-to-nobody" } },
            [],
            null,
            TestContext.Current.CancellationToken);

        // Assert.
        var rejected = Assert.IsType<RejectedContextLevel>(result);
        Assert.Equal("This item is no longer available.", rejected.Reason);
        Assert.DoesNotContain("mindmap", rejected.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, first.Asked);
        Assert.Equal(1, second.Asked);
    }
}
