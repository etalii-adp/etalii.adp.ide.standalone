using System.Threading.Channels;
using Grpc.Core;
using Serilog;

namespace EtAlii.Adp.Projects;

/// <summary>
/// The bound on what a tab's streams hold for it, and what happens when a tab stops reading - the
/// one place both are said (code-structure FR-070, B-28).
/// </summary>
/// <remarks>
/// <para>
/// <b>A tab that stops reading must not make the server hold everything that happens meanwhile.</b>
/// Every source a stream carries - the hierarchy, the context, each diagram's deltas, each table's
/// changes - is raised into a channel of its own and read from there into the tab's one stream. Each
/// holds at most <see cref="Capacity"/> messages.
/// </para>
/// <para>
/// <b>A source's channel that is full ends the stream it feeds</b> (<see cref="Ending{T}"/>): its
/// writers are events raised under a module's lock, which may not wait, and dropping a message
/// silently leaves the tab showing something that is no longer true. Ended, the stream is opened
/// again by the tab, and its fresh baseline is the truth. The tab's own channel instead makes its
/// writers wait (<see cref="Waiting{T}"/>): they are the readers of the source channels, so while
/// they wait those fill behind them and end.
/// </para>
/// </remarks>
public static class StreamBound
{
    private static readonly ILogger _logger = Log.ForContext(typeof(StreamBound));

    /// <summary>The most messages a stream's channel holds for a tab that is not reading.</summary>
    public const int Capacity = 1_000;

    /// <summary>
    /// A source's channel: one reader, and writers that never wait. A write that finds it full ends
    /// it with <see cref="Overflow"/> instead, which its reader meets once it has read what is held.
    /// </summary>
    /// <param name="stream">What the log calls the stream when it ends, such as "Context stream on watch 1Ab".</param>
    public static Channel<T> Ending<T>(string stream) => new EndingChannel<T>(stream);

    /// <summary>A tab's own channel: one reader, and writers that wait while it is full.</summary>
    public static Channel<T> Waiting<T>() =>
        Channel.CreateBounded<T>(new BoundedChannelOptions(Capacity) { FullMode = BoundedChannelFullMode.Wait, SingleReader = true });

    /// <summary>
    /// What a stream ends with when its channel was full: not a permanent refusal, so the tab opens it
    /// again. The reason names no stream or path, since it crosses the wire.
    /// </summary>
    private static RpcException Overflow() =>
        new(new Status(StatusCode.ResourceExhausted, $"The stream fell {Capacity:N0} messages behind and was ended; open it again for a fresh baseline."));

    private sealed class EndingChannel<T> : Channel<T>
    {
        public EndingChannel(string stream)
        {
            var inner = Channel.CreateBounded<T>(new BoundedChannelOptions(Capacity) { FullMode = BoundedChannelFullMode.Wait, SingleReader = true });
            Reader = inner.Reader;
            Writer = new EndingWriter<T>(inner.Writer, stream);
        }
    }

    private sealed class EndingWriter<T>(ChannelWriter<T> inner, string stream) : ChannelWriter<T>
    {
        public override bool TryWrite(T item)
        {
            if (inner.TryWrite(item))
            {
                return true;
            }

            // False for a channel already completed too, which is a stream closing, not a tab behind.
            if (inner.TryComplete(Overflow()))
            {
                _logger.Warning("{Stream} held {Capacity} messages its tab had not read; ending it so the tab opens it again", stream, Capacity);
            }

            return false;
        }

        public override bool TryComplete(Exception? error = null) => inner.TryComplete(error);

        // Never waits, like TryWrite: a full channel is one to write to and so end, not to wait for.
        public override ValueTask<bool> WaitToWriteAsync(CancellationToken cancellationToken = default)
        {
            var waiting = inner.WaitToWriteAsync(cancellationToken);
            return waiting.IsCompleted ? waiting : ValueTask.FromResult(true);
        }

        public override ValueTask WriteAsync(T item, CancellationToken cancellationToken = default) =>
            TryWrite(item) ? ValueTask.CompletedTask : ValueTask.FromException(new ChannelClosedException(Overflow()));
    }
}
