using EtAlii.Adp.Context.Wire;
using EtAlii.Adp.Hierarchy.Wire;
using Grpc.Core;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.TestSupport.Host;

/// <summary>
/// Waits until a server stream is really running, so nothing a flow test does next can happen
/// before the server has subscribed.
/// </summary>
/// <remarks>
/// <para>
/// A watch call returns before the server has run it, and a change made in that gap is never
/// reported. The flow tests used to cover the gap with a fixed half second, which a loaded runner
/// outlasts: the test then waits out its timeout on a change that cannot come. That is how
/// <c>UndoRedoFlowTests</c> failed on GitHub's runners, and every test that changes something
/// under a stream it has just opened has the same gap.
/// </para>
/// <para>
/// A stream that sends nothing when it opens can only be seen running by making it report
/// something. So a probe is provoked, and provoked again, until the stream reports one. A stream
/// that sends a baseline when it opens provokes itself: its first message is the probe.
/// </para>
/// </remarks>
public static class StreamProbe
{
    /// <summary>What every hierarchy probe file's name starts with.</summary>
    private const string HierarchyProbePrefix = "watch-probe-";

    private static readonly TimeSpan ProbeInterval = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// Returns once <paramref name="stream"/> has reported the last probe <paramref name="provoke"/>
    /// made, as <paramref name="recognize"/> tells it.
    /// </summary>
    /// <remarks>
    /// The <i>last</i> probe rather than the first one seen: a probe the stream was slow to report
    /// would otherwise still be on the stream when the test starts reading for its own message.
    /// What may still follow is a message about something other than the last probe, so a test
    /// reads until the message it wants rather than asserting on the next one.
    /// </remarks>
    /// <param name="stream">The server stream a test has just opened.</param>
    /// <param name="provoke">Makes the server report something on the stream and returns what identifies that report.</param>
    /// <param name="recognize">Whether a message on the stream is the report of the given probe.</param>
    /// <param name="cancellationToken">Cancels the wait; a test passes its message timeout.</param>
    private static async Task WaitUntilLiveAsync<TMessage, TProbe>(
        IAsyncStreamReader<TMessage> stream,
        Func<CancellationToken, Task<TProbe>> provoke,
        Func<TMessage, TProbe, bool> recognize,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(provoke);
        ArgumentNullException.ThrowIfNull(recognize);

        Task<bool>? pending = null;
        while (true)
        {
            var probe = await provoke(cancellationToken);

            var patience = Task.Delay(ProbeInterval, cancellationToken);
            while (true)
            {
                pending ??= stream.MoveNext(cancellationToken);
                if (await Task.WhenAny(pending, patience) != pending)
                {
                    await patience;
                    break;
                }

                var moved = await pending;
                pending = null;
                if (!moved)
                {
                    throw new InvalidOperationException("The stream ended before it reported a probe.");
                }

                if (recognize(stream.Current, probe))
                {
                    return;
                }
            }
        }
    }

    /// <summary>
    /// Returns once a hierarchy watch has reported the last probe file created in
    /// <paramref name="folder"/>, which must be a folder the watch's connection has listed.
    /// </summary>
    /// <remarks>
    /// The watch sends nothing when it opens; a probe file is the harmless change it reports. Probe
    /// files are created until one is reported, because a file created before the watcher exists is
    /// seen by nothing. Another connection's probes may still arrive on a watch that shares the
    /// folder.
    /// </remarks>
    public static Task WaitUntilHierarchyLiveAsync(IAsyncStreamReader<HierarchyMessage> stream, string folder, CancellationToken cancellationToken) =>
        WaitUntilLiveAsync(
            stream,
            async token =>
            {
                var probe = $"{HierarchyProbePrefix}{Guid.NewGuid():N}.txt";
                await File.WriteAllTextAsync(IoPath.Combine(folder, probe), "", token);
                return probe;
            },
            IsCreationOf,
            cancellationToken);

    /// <summary>
    /// Returns once a context watch has sent its first message, which proves the server has
    /// registered the connection: selections, prompts and the project's actions all reach it from
    /// then on.
    /// </summary>
    /// <remarks>
    /// The context watch provokes itself. Registering the connection queues its baseline before the
    /// server starts writing the stream, and the selection and the prompt writers are both
    /// registered by then, so the baseline's first message cannot arrive before either. Only that
    /// first message is read; the rest of the baseline stays on the stream, and a test reads past
    /// it to the message it wants.
    /// </remarks>
    public static async Task WaitUntilContextLiveAsync(IAsyncStreamReader<ContextMessage> stream, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);

        if (!await stream.MoveNext(cancellationToken))
        {
            throw new InvalidOperationException("The context stream ended before its baseline arrived.");
        }
    }

    private static bool IsCreationOf(HierarchyMessage message, string probe) =>
        message.MessageCase == HierarchyMessage.MessageOneofCase.Change
        && message.Change.ChangeCase == HierarchyChange.ChangeOneofCase.Created
        && message.Change.Created.Entry.Name == probe;
}
