using EtAlii.Adp.Hierarchy.Wire;
using Grpc.Core;

namespace EtAlii.Adp.Backend.Tests;

/// <summary>
/// Waits until a hierarchy watch is really running, so nothing a flow test does next can happen
/// before the server has subscribed.
/// </summary>
/// <remarks>
/// <para>
/// <c>WatchHierarchy</c> returns before the server has run it, and a change made in that gap is
/// never reported. The flow tests used to cover the gap with a fixed half second, which a loaded
/// runner outlasts: the test then waits out its timeout on a change that cannot come. That is how
/// <c>UndoRedoFlowTests</c> failed on GitHub's runners, and every test that renames, deletes or
/// creates under a watch it has just opened has the same gap.
/// </para>
/// <para>
/// The watch sends nothing when it opens, so the only way to see it running is to make it report
/// something. Probe files are created until one is reported, because a file created before the
/// watcher exists is seen by nothing.
/// </para>
/// </remarks>
internal static class HierarchyWatchProbe
{
    /// <summary>What every probe file's name starts with, for a test that must look past them.</summary>
    public const string Prefix = "watch-probe-";

    private static readonly TimeSpan ProbeInterval = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// Returns once <paramref name="stream"/> has reported the last probe created in
    /// <paramref name="folder"/>, which must be a folder the watch's connection has listed.
    /// </summary>
    /// <remarks>
    /// The <i>last</i> probe rather than the first one seen: a probe the watch was slow to report
    /// would otherwise still be on the stream when the test starts reading for its own change.
    /// What may still follow is something other than a creation about a probe, and another
    /// connection's probes on a watch that shares the folder - so a test reads until the change it
    /// wants rather than asserting on the next message.
    /// </remarks>
    public static async Task WaitUntilLiveAsync(IAsyncStreamReader<HierarchyMessage> stream, string folder, CancellationToken cancellationToken)
    {
        Task<bool>? pending = null;
        while (true)
        {
            var probe = $"{Prefix}{Guid.NewGuid():N}.txt";
            await File.WriteAllTextAsync(Path.Combine(folder, probe), "", cancellationToken);

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
                    throw new InvalidOperationException("The hierarchy stream ended before the watch reported anything.");
                }

                if (IsCreationOf(stream.Current, probe))
                {
                    return;
                }
            }
        }
    }

    /// <summary>Whether <paramref name="message"/> reports the creation of a probe file.</summary>
    public static bool IsProbe(HierarchyMessage message) =>
        message.MessageCase == HierarchyMessage.MessageOneofCase.Change
        && message.Change.ChangeCase == HierarchyChange.ChangeOneofCase.Created
        && message.Change.Created.Entry.Name.StartsWith(Prefix, StringComparison.Ordinal);

    private static bool IsCreationOf(HierarchyMessage message, string probe) =>
        IsProbe(message) && message.Change.Created.Entry.Name == probe;
}
