using Grpc.Core;
using Xunit;

namespace EtAlii.Adp.Projects.Tests;

/// <summary>
/// What a stream's channel holds for a tab that stops reading, and what happens once it is full
/// (code-structure FR-070, B-28): the tab is given what was held, and then the stream ends with a
/// status it opens the stream again from.
/// </summary>
public class StreamBoundTests
{
    [Fact]
    public async Task AnEndingChannel_HoldsTheBound_ThenEndsWithAnOverflow_ThatIsNotPermanent()
    {
        // Arrange: a reader that has stopped.
        var channel = StreamBound.Ending<int>("Test stream");

        // Act.
        var taken = Enumerable.Range(0, StreamBound.Capacity + 1).Select(channel.Writer.TryWrite).ToArray();
        var afterTheEnd = channel.Writer.TryWrite(-1);

        // Assert: everything up to the bound was held, the write past it was not, and nothing after.
        Assert.All(taken[..StreamBound.Capacity], Assert.True);
        Assert.False(taken[StreamBound.Capacity]);
        Assert.False(afterTheEnd);

        var read = new List<int>();
        var ended = await Assert.ThrowsAsync<RpcException>(() => ReadAll().WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken));
        Assert.Equal(Enumerable.Range(0, StreamBound.Capacity), read);
        Assert.Equal(StatusCode.ResourceExhausted, ended.StatusCode);
        Assert.False(PermanentRefusal.IsPermanent(ended.StatusCode));

        async Task ReadAll()
        {
            await foreach (var item in channel.Reader.ReadAllAsync(TestContext.Current.CancellationToken))
            {
                read.Add(item);
            }
        }
    }

    [Fact]
    public async Task AWaitingChannel_HoldsTheBound_AndMakesTheNextWriterWait()
    {
        // Arrange.
        var channel = StreamBound.Waiting<int>();
        for (var i = 0; i < StreamBound.Capacity; i++)
        {
            await channel.Writer.WriteAsync(i, TestContext.Current.CancellationToken);
        }

        // Act.
        var next = channel.Writer.WriteAsync(StreamBound.Capacity, TestContext.Current.CancellationToken).AsTask();

        // Assert: waiting, and taken once the reader reads.
        Assert.False(next.IsCompleted);
        Assert.True(channel.Reader.TryRead(out _));
        await next.WaitAsync(TestContext.Current.CancellationToken);
    }
}
