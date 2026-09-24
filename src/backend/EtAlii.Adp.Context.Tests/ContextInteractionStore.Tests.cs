using System.Threading.Channels;
using EtAlii.Adp.Context.Wire;
using EtAlii.Adp.Documents.Wire;
using Xunit;

namespace EtAlii.Adp.Context.Tests;

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
        RootPath = @"C:\root",
        WatchId = watchId,
        Target = new ContextTarget(ContextScope.Hierarchy, @"C:\root\item.txt", IsContainer: false, ShortGuid.NewShortGuid()),
        ActionId = "stub.action",
        Provider = new ContextInteractionStoreStubProvider(),
    };

    [Fact]
    public void TryPush_ReachesOnlyTheOwningConnectionsWriter()
    {
        // Arrange.
        var store = new ContextInteractionStore();
        var watchIdA = ShortGuid.NewShortGuid();
        var watchIdB = ShortGuid.NewShortGuid();
        var channelA = Channel.CreateUnbounded<ContextMessage>();
        var channelB = Channel.CreateUnbounded<ContextMessage>();
        store.Register(watchIdA, channelA.Writer);
        store.Register(watchIdB, channelB.Writer);

        // Act.
        var pushed = store.TryPush(watchIdA, Prompt(ShortGuid.NewShortGuid()));

        // Assert.
        Assert.True(pushed);
        Assert.True(channelA.Reader.TryRead(out var messageA));
        Assert.Equal(ContextMessage.MessageOneofCase.Prompt, messageA.MessageCase);
        Assert.False(channelB.Reader.TryRead(out _));
    }

    [Fact]
    public void TryPush_ForAWatchIdThatNeverRegistered_FailsCleanly()
    {
        // Arrange.
        var store = new ContextInteractionStore();

        // Act.
        var pushed = store.TryPush(ShortGuid.NewShortGuid(), Prompt(ShortGuid.NewShortGuid()));

        // Assert.
        Assert.False(pushed);
    }

    [Fact]
    public void Remove_DiscardsBothTheWriterAndThatConnectionsInFlightInteractions()
    {
        // Arrange.
        var store = new ContextInteractionStore();
        var watchId = ShortGuid.NewShortGuid();
        var interactionId = ShortGuid.NewShortGuid();
        var channel = Channel.CreateUnbounded<ContextMessage>();
        store.Register(watchId, channel.Writer);
        store.Begin(Interaction(watchId, interactionId));

        // Act.
        store.Remove(watchId);

        // Assert.
        Assert.Null(store.Get(interactionId));
        Assert.False(store.TryPush(watchId, Prompt(interactionId)));
    }

    [Fact]
    public void Remove_LeavesAnotherConnectionsInteractionsUntouched()
    {
        // Arrange.
        var store = new ContextInteractionStore();
        var watchIdA = ShortGuid.NewShortGuid();
        var watchIdB = ShortGuid.NewShortGuid();
        var interactionB = ShortGuid.NewShortGuid();
        store.Register(watchIdA, Channel.CreateUnbounded<ContextMessage>().Writer);
        store.Register(watchIdB, Channel.CreateUnbounded<ContextMessage>().Writer);
        store.Begin(Interaction(watchIdB, interactionB));

        // Act.
        store.Remove(watchIdA);

        // Assert.
        Assert.NotNull(store.Get(interactionB));
    }

    [Fact]
    public void Complete_RemovesTheInteraction_SoARepeatedSubmitFindsNothingToRunAgain()
    {
        // Arrange.
        var store = new ContextInteractionStore();
        var watchId = ShortGuid.NewShortGuid();
        var interactionId = ShortGuid.NewShortGuid();
        store.Register(watchId, Channel.CreateUnbounded<ContextMessage>().Writer);
        store.Begin(Interaction(watchId, interactionId));

        // Act.
        store.Complete(interactionId);
        store.Complete(interactionId);

        // Assert.
        Assert.Null(store.Get(interactionId));
    }

    [Fact]
    public void Begin_ForAConnectionWithNoOpenStream_RecordsNothing()
    {
        // Arrange.
        var store = new ContextInteractionStore();
        var watchId = ShortGuid.NewShortGuid();
        var interactionId = ShortGuid.NewShortGuid();

        // Act.
        store.Begin(Interaction(watchId, interactionId));

        // Assert.
        Assert.Null(store.Get(interactionId));
    }

}
