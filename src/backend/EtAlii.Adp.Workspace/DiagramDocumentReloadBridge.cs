using System.Collections.Concurrent;
using System.Threading.Channels;
using EtAlii.Adp.Diagram;
using EtAlii.Adp.Files;
using Serilog;

namespace EtAlii.Adp.Workspace;

/// <summary>
/// The watcher-to-store bridge: the one place a disk change reaches a diagram document store's
/// <c>ReloadAsync</c>. <c>DiagramService.Open</c> reports every diagram it opens; this subscribes to
/// <see cref="ProjectFileEvents"/> once per project root and, when a tracked body
/// or registration file changes underneath, hands the owning module's
/// <see cref="IDiagramDocumentReloader"/> the body to re-read. The stores push the result to
/// their sessions themselves - the bridge only tells them the disk moved (modular-text-editors
/// Requirements 5.3, 5.5).
/// </summary>
/// <remarks>
/// Tracked paths and subscriptions live for the process, deliberately outliving the streams that
/// opened them: the stores cache documents by path for the process too, so an external write
/// landing while no session is open would otherwise leave a closed-and-reopened diagram on the
/// stale pre-change model until a restart.
/// <para>
/// <b>The watcher's thread only notes what changed; one loop reads it</b> (code-structure FR-050,
/// FR-070). A read that meets a body still being written waits out its retries without holding the
/// thread every subscriber of the root hears its events on. Each tracked body has at most one
/// waiting step, the latest event's, so what waits is bounded by the documents tracked: a burst of
/// changes to one body is answered by one read of what the disk then holds, which is all an earlier
/// event's read would have found too. One loop rather than a task per event, so two reads of one
/// body can never finish in the wrong order and leave the older one shown.
/// </para>
/// </remarks>
public sealed class DiagramDocumentReloadBridge : IDisposable
{
    private static readonly ILogger _logger = Log.ForContext<DiagramDocumentReloadBridge>();

    private readonly IReadOnlyDictionary<ToolOrigin, IDiagramDocumentReloader> _reloaders;
    private readonly ProjectFileEvents _events;

    // The three maps are concurrent dictionaries, so each single read or write of one is atomic
    // without the lock, and the event callbacks read them from the watcher's thread without it.
    // _subscriptionLock serializes only the compound steps on a subscription - check, subscribe,
    // dispose - so that two Opens never subscribe twice for one root.
    private readonly ConcurrentDictionary<string, TrackedDocument> _documents = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, string> _registrations = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<ProjectRoot, IDisposable> _subscriptions = new();
    private readonly Lock _subscriptionLock = new();
    private bool _disposed;

    // What each tracked body waits for, the latest event's step only, and the one wake that tells the
    // loop to look: a wake already waiting covers any number of later ones, so it drops them.
    private readonly ConcurrentDictionary<string, Pending> _pending = new(StringComparer.OrdinalIgnoreCase);
    private readonly Channel<bool> _wake = Channel.CreateBounded<bool>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite, SingleReader = true });
    private readonly CancellationTokenSource _stopping = new();

    private sealed record TrackedDocument(ProjectRoot Root, string BodyPath, IDiagramDocumentReloader Reloader);

    /// <summary>What one body waits for: a re-read, its deletion, or - after lost events - whichever the disk says.</summary>
    private enum Step
    {
        Reload,
        Deleted,
        AskTheDisk,
    }

    private sealed record Pending(TrackedDocument Document, Step Step);

    public DiagramDocumentReloadBridge(IEnumerable<IDiagramDocumentReloader> reloaders, ProjectFileEvents events)
    {
        ArgumentNullException.ThrowIfNull(reloaders);
        ArgumentNullException.ThrowIfNull(events);
        _reloaders = reloaders.ToDictionary(reloader => reloader.Origin);
        _events = events;
        Completion = Task.Run(() => RunAsync(_stopping.Token), CancellationToken.None);
    }

    /// <summary>The loop that reads what the watcher noted; it ends once the bridge is disposed.</summary>
    public Task Completion { get; }

    /// <summary>
    /// Starts caring about one opened diagram: its body, the <c>.adp</c> it was opened through
    /// (when there is one), and the root to watch them under. A type that registered no
    /// reloader is left alone - the ansible module watches its own folder already.
    /// </summary>
    public void Track(ProjectRoot root, string bodyPath, string? registrationPath, ToolOrigin origin)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root.FullPath, nameof(root));
        ArgumentException.ThrowIfNullOrWhiteSpace(bodyPath);
        ArgumentNullException.ThrowIfNull(origin);

        if (!_reloaders.TryGetValue(origin, out var reloader))
        {
            return;
        }

        // ReSharper disable once InconsistentlySynchronizedField - Reason: one atomic ConcurrentDictionary write; _subscriptionLock serializes subscribing and unsubscribing, not the tracked set.
        _documents[bodyPath] = new TrackedDocument(root, bodyPath, reloader);
        if (registrationPath is { Length: > 0 })
        {
            // ReSharper disable once InconsistentlySynchronizedField - Reason: one atomic ConcurrentDictionary write; _subscriptionLock serializes subscribing and unsubscribing, not the tracked set.
            _registrations[registrationPath] = bodyPath;
        }

        EnsureWatching(root);
    }

    private void EnsureWatching(ProjectRoot root)
    {
        // ReSharper disable once InconsistentlySynchronizedField - Reason: an atomic ConcurrentDictionary read as a fast path; a miss is decided again under _subscriptionLock below, and a hit orders before any concurrent replace or dispose exactly as a locked read would.
        if (_subscriptions.ContainsKey(root))
        {
            return;
        }

        // Under a lock rather than GetOrAdd: two Opens racing here would each subscribe, and
        // GetOrAdd disposes neither loser.
        lock (_subscriptionLock)
        {
            if (_disposed || _subscriptions.ContainsKey(root))
            {
                return;
            }

            try
            {
                _subscriptions[root] = _events.Subscribe(root.FullPath, heard => OnChange(root, heard));
            }
            catch (Exception exception) when (exception is ArgumentException or IOException)
            {
                // A root that cannot be watched - deleted underneath, an inaccessible share -
                // degrades to the old behavior for that project: reloads on restart only.
                _logger.Warning(exception, "Cannot watch {RootPath}; external edits there will not reach open diagrams", root.FullPath);
            }
        }
    }

    /// <summary>
    /// One file event, normalized: a save that replaced the body arrives as its Created or
    /// Changed, never as a rename away (see <see cref="ProjectFileEvents"/>). A body that is gone is
    /// an empty diagram, not the last one kept alive - but a body MISSING is not yet a body gone, so
    /// the store is told which it is by the event, not by a read.
    /// </summary>
    /// <remarks>
    /// So a store keeps its last good document through a read that fails (the body missing for an
    /// instant mid-publish) and empties it only on <see cref="IDiagramDocumentReloader.BodyDeletedAsync"/>.
    /// <para>
    /// KNOWN LIMITS. An editor that saves by deleting the body and then creating it shows the
    /// diagram empty between its two events; the Created heals it. A rename-away to a name that is
    /// not a writer's own transient file - another tool's own safe-write scheme - is read as a
    /// delete and heals the same way when the new body arrives.
    /// </para>
    /// </remarks>
    private void OnChange(ProjectRoot root, FileEvent heard)
    {
        if (heard.Kind == FileEventKind.Lost)
        {
            ReadEverythingAgain(root);
            return;
        }

        var oldPath = heard.Kind switch
        {
            FileEventKind.Deleted => heard.Path,
            FileEventKind.Renamed => heard.OldPath,
            _ => null,
        };
        var newPath = heard.Kind == FileEventKind.Deleted ? null : heard.Path;

        if (oldPath is not null && _documents.TryGetValue(oldPath, out var deleted))
        {
            _logger.Debug("{BodyPath} is gone after a {Kind}", deleted.BodyPath, heard.Kind);
            Note(deleted, Step.Deleted);
        }
        else
        {
            ReloadIfTracked(oldPath);
        }

        if (!string.Equals(oldPath, newPath, StringComparison.OrdinalIgnoreCase))
        {
            ReloadIfTracked(newPath);
        }
    }

    private void ReloadIfTracked(string? path)
    {
        if (path is null)
        {
            return;
        }

        // A registration change re-reads its body: the body's content is what the sessions
        // show, and the store re-delivering is what carries a rerouted or re-viewed .adp to
        // the next open anyway.
        if (!_documents.TryGetValue(path, out var document) && !(_registrations.TryGetValue(path, out var bodyPath) && _documents.TryGetValue(bodyPath, out document)))
        {
            return;
        }

        _logger.Debug("Reloading {BodyPath} after a disk change to {Path}", document.BodyPath, path);
        Note(document, Step.Reload);
    }

    /// <summary>
    /// The watcher lost events - a buffer overflow above all - so the stale answers it allowed
    /// are corrected the blunt way: re-read every tracked document under its root. Reload tells
    /// the sessions, so nothing stays quietly behind the disk.
    /// </summary>
    private void ReadEverythingAgain(ProjectRoot root)
    {
        _logger.Warning("The watcher on {RootPath} lost events; re-reading every tracked diagram there", root.FullPath);

        foreach (var document in _documents.Values.Where(candidate => candidate.Root == root))
        {
            Note(document, Step.AskTheDisk);
        }
    }

    /// <summary>Notes the step <paramref name="document"/> waits for, folding in one it waited for already, and wakes the loop.</summary>
    private void Note(TrackedDocument document, Step step)
    {
        _pending.AddOrUpdate(document.BodyPath, new Pending(document, step), (_, waiting) => new Pending(document, Folded(waiting.Step, step)));
        _wake.Writer.TryWrite(true);
    }

    /// <summary>
    /// The latest event decides, except that a re-read does not undo a deletion still waiting: a
    /// store keeps its last good document through a read that cannot find the body, so a delete
    /// followed by a change to the registration would leave the deleted diagram drawn. The disk
    /// says which it is by then, as it does after lost events.
    /// </summary>
    private static Step Folded(Step waiting, Step heard) =>
        heard == Step.Reload && waiting != Step.Reload ? Step.AskTheDisk : heard;

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (await _wake.Reader.WaitToReadAsync(cancellationToken))
            {
                _wake.Reader.TryRead(out _);
                foreach (var bodyPath in _pending.Keys)
                {
                    // Taken before it runs: an event heard while it runs notes a fresh step, and wakes
                    // the loop for it.
                    if (_pending.TryRemove(bodyPath, out var pending))
                    {
                        await TakeAsync(pending, cancellationToken);
                    }
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Disposed: what still waited goes unread, as it would with no watcher.
        }
    }

    private static async Task TakeAsync(Pending pending, CancellationToken cancellationToken)
    {
        var document = pending.Document;

        // Guarded one by one: a document that no longer parses must cost its own reload and
        // nothing else's - and certainly not the loop every other document waits on.
        try
        {
            // After lost events the delete may have been among them, so absence is asked of the
            // disk, having no event to ask. KNOWN LIMIT: an overflow landing inside another
            // program's File.Replace can find the body renamed away and empty the diagram. If the
            // replace's rename onto the body was among the lost events too, it stays empty until
            // the body next changes or the diagram is reopened.
            var deleted = pending.Step == Step.Deleted || (pending.Step == Step.AskTheDisk && !File.Exists(document.BodyPath));
            if (deleted)
            {
                await document.Reloader.BodyDeletedAsync(document.Root, document.BodyPath, cancellationToken);
            }
            else
            {
                await document.Reloader.ReloadAsync(document.Root, document.BodyPath, cancellationToken);
            }
        }
        catch (Exception reload) when (reload is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _logger.Error(reload, "Re-reading {BodyPath} after a disk change did not succeed", document.BodyPath);
        }
    }

    public void Dispose()
    {
        lock (_subscriptionLock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _stopping.Cancel();
            foreach (var subscription in _subscriptions.Values)
            {
                subscription.Dispose();
            }

            _subscriptions.Clear();
        }
    }
}
