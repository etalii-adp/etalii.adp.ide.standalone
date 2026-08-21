using System.Threading.Channels;
using EtAlii.Adp.Backend.Context;
using Xunit;

namespace EtAlii.Adp.Backend.Tests;

/// <summary>
/// Uses real channels rather than a mocked writer, so these tests exercise exactly what
/// <c>WatchHierarchy</c> hands the store.
/// </summary>
public class ContextInteractionStoreTests
{
    private static ContextPrompt Prompt(ShortGuid interactionId) =>
        new() { InteractionId = interactionId, Closed = new ContextInteractionClosed { Message = "test" } };

    private static ContextInteraction Interaction(ShortGuid watchId, ShortGuid interactionId) => new()
    {
        Id = interactionId,
        WatchId = watchId,
        Target = new ContextTarget(ContextScope.Hierarchy, @"C:\root\item.txt", IsContainer: false, ShortGuid.NewShortGuid()),
        ActionId = "stub.action",
        Provider = new StubProvider(),
    };

    [Fact]
    public void TryPush_ReachesOnlyTheOwningConnectionsWriter()
    {
        var store = new ContextInteractionStore();
        var watchIdA = ShortGuid.NewShortGuid();
        var watchIdB = ShortGuid.NewShortGuid();
        var channelA = Channel.CreateUnbounded<HierarchyMessage>();
        var channelB = Channel.CreateUnbounded<HierarchyMessage>();
        store.Register(watchIdA, channelA.Writer);
        store.Register(watchIdB, channelB.Writer);

        var pushed = store.TryPush(watchIdA, Prompt(ShortGuid.NewShortGuid()));

        Assert.True(pushed);
        Assert.True(channelA.Reader.TryRead(out var messageA));
        Assert.Equal(HierarchyMessage.MessageOneofCase.Prompt, messageA.MessageCase);
        Assert.False(channelB.Reader.TryRead(out _));
    }

    [Fact]
    public void TryPush_ForAWatchIdThatNeverRegistered_FailsCleanly()
    {
        var store = new ContextInteractionStore();

        var pushed = store.TryPush(ShortGuid.NewShortGuid(), Prompt(ShortGuid.NewShortGuid()));

        Assert.False(pushed);
    }

    [Fact]
    public void Remove_DiscardsBothTheWriterAndThatConnectionsInFlightInteractions()
    {
        var store = new ContextInteractionStore();
        var watchId = ShortGuid.NewShortGuid();
        var interactionId = ShortGuid.NewShortGuid();
        var channel = Channel.CreateUnbounded<HierarchyMessage>();
        store.Register(watchId, channel.Writer);
        store.Begin(Interaction(watchId, interactionId));

        store.Remove(watchId);

        Assert.Null(store.Get(interactionId));
        Assert.False(store.TryPush(watchId, Prompt(interactionId)));
    }

    [Fact]
    public void Remove_LeavesAnotherConnectionsInteractionsUntouched()
    {
        var store = new ContextInteractionStore();
        var watchIdA = ShortGuid.NewShortGuid();
        var watchIdB = ShortGuid.NewShortGuid();
        var interactionB = ShortGuid.NewShortGuid();
        store.Register(watchIdA, Channel.CreateUnbounded<HierarchyMessage>().Writer);
        store.Register(watchIdB, Channel.CreateUnbounded<HierarchyMessage>().Writer);
        store.Begin(Interaction(watchIdB, interactionB));

        store.Remove(watchIdA);

        Assert.NotNull(store.Get(interactionB));
    }

    [Fact]
    public void Complete_RemovesTheInteraction_SoARepeatedSubmitFindsNothingToRunAgain()
    {
        var store = new ContextInteractionStore();
        var watchId = ShortGuid.NewShortGuid();
        var interactionId = ShortGuid.NewShortGuid();
        store.Register(watchId, Channel.CreateUnbounded<HierarchyMessage>().Writer);
        store.Begin(Interaction(watchId, interactionId));

        store.Complete(interactionId);
        store.Complete(interactionId);

        Assert.Null(store.Get(interactionId));
    }

    [Fact]
    public void Begin_ForAConnectionWithNoOpenStream_RecordsNothing()
    {
        var store = new ContextInteractionStore();
        var watchId = ShortGuid.NewShortGuid();
        var interactionId = ShortGuid.NewShortGuid();

        store.Begin(Interaction(watchId, interactionId));

        Assert.Null(store.Get(interactionId));
    }

    private sealed class StubProvider : IContextActionProvider
    {
        public ContextScope Scope => ContextScope.Hierarchy;

        public ValueTask<IReadOnlyList<ContextActionGroupDefinition>> DiscoverAsync(ContextTarget target, CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<ContextActionGroupDefinition>>(Array.Empty<ContextActionGroupDefinition>());

        public ValueTask<ContextExecutionResult> ExecuteAsync(ContextTarget target, string actionId, CancellationToken cancellationToken) =>
            ValueTask.FromResult<ContextExecutionResult>(new ContextExecutionResult.Completed());

        public ValueTask<ContextValidationResult> ValidateAsync(ContextTarget target, string actionId, string value, CancellationToken cancellationToken) =>
            ValueTask.FromResult(ContextValidationResult.Accepted);

        public ValueTask<ContextCommitResult> CommitAsync(ContextTarget target, string actionId, string value, CancellationToken cancellationToken) =>
            ValueTask.FromResult(ContextCommitResult.Succeeded);
    }
}
