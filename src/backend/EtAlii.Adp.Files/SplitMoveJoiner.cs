namespace EtAlii.Adp.Files;

/// <summary>
/// Puts back together a move that the Linux file system watcher reported as a Deleted followed
/// by a Created, so it reaches whoever listens as the one Renamed it was.
/// </summary>
/// <remarks>
/// <para>
/// inotify reports a move as two events, IN_MOVED_FROM and IN_MOVED_TO. .NET's
/// <see cref="FileSystemWatcher"/> pairs them into a Renamed only when the second is already
/// queued, or arrives within a 2 ms poll, after the first; otherwise it raises a Deleted for the
/// old path and, when the second does arrive, a Created for the new one (a limit its own source
/// documents, in <c>FileSystemWatcher.Linux.cs</c>). On a loaded machine the process doing the
/// move can easily be off the CPU for longer than that between the two halves. Taken at face
/// value, the Deleted removes the entry - and a selection tracking it is cleared - and the
/// Created makes a new entry with a new id: a file renamed on disk loses its selection and its
/// identity. That was the intermittent CI failure of
/// <c>ContextSelectionFlowTests.RenameOnDisk_PushesTheSelectionWithItsNewPathAndTheSameId</c>.
/// </para>
/// <para>
/// So a Deleted whose path is gone is held back for a short grace. When the very next event is a
/// Created of a path that exists, while the deleted path still does not, the two are delivered as
/// one Renamed - the same "next event" pairing the watcher itself applies, given more patience.
/// Anything else delivers the held Deleted first, in order, and the grace running out delivers it
/// on its own, so a real delete is reported at most a grace late. A Deleted whose path is still on
/// disk is not held: it is not half of a move, and the model knows what to make of it.
/// </para>
/// <para>
/// KNOWN LIMIT. A real delete followed within the grace by the creation of an unrelated file, with
/// nothing in between, is read as a rename: the new file takes the deleted one's entry and id.
/// Disk alone cannot tell the two apart once the inotify cookie is gone; the watcher makes the same
/// call within its own 2 ms.
/// </para>
/// </remarks>
public sealed class SplitMoveJoiner : IDisposable
{
    /// <summary>How long a Deleted waits for the Created that may complete its move.</summary>
    private static readonly TimeSpan DefaultGrace = TimeSpan.FromMilliseconds(250);

    private readonly Action<FileEvent> _deliver;
    private readonly TimeSpan _grace;
    private readonly TimeProvider _timeProvider;
    private readonly Lock _gate = new();

    // The held Deleted, and the timer that delivers it alone when nothing completes it. Only ever
    // touched under _gate, which is also held while delivering - so events reach the callback one
    // at a time and in the order they happened, whichever thread (the watcher's or the timer's)
    // delivers them.
    private string? _pendingDeletion;
    private ITimer? _pendingTimer;
    private bool _disposed;

    /// <param name="deliver">Receives the events, joined where a move was split.</param>
    /// <param name="grace">How long a Deleted waits; <see cref="DefaultGrace"/> when not given.</param>
    /// <param name="timeProvider">The clock the grace runs on; the system clock when not given.</param>
    public SplitMoveJoiner(Action<FileEvent> deliver, TimeSpan? grace = null, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(deliver);
        _deliver = deliver;
        _grace = grace ?? DefaultGrace;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>Takes one event as the watcher raised it.</summary>
    public void Post(FileEvent heard)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            if (_pendingDeletion is { } deleted)
            {
                ClearPending();
                if (heard.Kind == FileEventKind.Created && Path.Exists(heard.Path) && !Path.Exists(deleted))
                {
                    _deliver(new FileEvent(FileEventKind.Renamed, heard.Path, deleted));
                    return;
                }

                _deliver(new FileEvent(FileEventKind.Deleted, deleted));
            }

            if (heard.Kind == FileEventKind.Deleted && !Path.Exists(heard.Path))
            {
                _pendingDeletion = heard.Path;
                _pendingTimer = _timeProvider.CreateTimer(_ => Flush(), null, _grace, Timeout.InfiniteTimeSpan);
                return;
            }

            _deliver(heard);
        }
    }

    /// <summary>
    /// Delivers a held Deleted now, when the grace runs out. A watcher error needs no call of its
    /// own: posted as a Lost, it delivers a deletion still waiting before it.
    /// </summary>
    private void Flush()
    {
        lock (_gate)
        {
            if (_disposed || _pendingDeletion is not { } deleted)
            {
                return;
            }

            ClearPending();
            _deliver(new FileEvent(FileEventKind.Deleted, deleted));
        }
    }

    /// <summary>Drops a held Deleted: the watch it belonged to has ended.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            ClearPending();
        }
    }

    private void ClearPending()
    {
        _pendingTimer?.Dispose();
        _pendingTimer = null;
        _pendingDeletion = null;
    }
}
