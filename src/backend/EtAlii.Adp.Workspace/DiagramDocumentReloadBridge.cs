using System.Collections.Concurrent;
using EtAlii.Adp.Diagram;
using EtAlii.Adp.Files;
using Serilog;

namespace EtAlii.Adp.Workspace;

/// <summary>
/// The watcher-to-store bridge: the one place a disk change reaches a diagram document store's
/// <c>Reload</c>. <c>DiagramService.Open</c> reports every diagram it opens; this subscribes to
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

    private sealed record TrackedDocument(ProjectRoot Root, string BodyPath, IDiagramDocumentReloader Reloader);

    public DiagramDocumentReloadBridge(IEnumerable<IDiagramDocumentReloader> reloaders, ProjectFileEvents events)
    {
        ArgumentNullException.ThrowIfNull(reloaders);
        ArgumentNullException.ThrowIfNull(events);
        _reloaders = reloaders.ToDictionary(reloader => reloader.Origin);
        _events = events;
    }

    /// <summary>
    /// Starts caring about one opened diagram: its body, the <c>.adp</c> it was opened through
    /// (when there is one), and the root to watch them under. A type that registered no
    /// reloader is left alone - the ansible module watches its own folder already.
    /// </summary>
    public void Track(string rootPath, string bodyPath, string? registrationPath, ToolOrigin origin)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(bodyPath);
        ArgumentNullException.ThrowIfNull(origin);

        if (!_reloaders.TryGetValue(origin, out var reloader))
        {
            return;
        }

        // ReSharper disable once InconsistentlySynchronizedField - Reason: one atomic ConcurrentDictionary write; _subscriptionLock serializes subscribing and unsubscribing, not the tracked set.
        var root = ProjectRoot.From(rootPath);
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
    /// instant mid-publish) and empties it only on <see cref="IDiagramDocumentReloader.BodyDeleted"/>.
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
            deleted.Reloader.BodyDeleted(deleted.Root.FullPath, deleted.BodyPath);
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
        document.Reloader.Reload(document.Root.FullPath, document.BodyPath);
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
            // Guarded one by one: this runs on the watcher's thread, where a document that no
            // longer parses must cost its own reload and nothing else's - and certainly not
            // the process.
            try
            {
                // The lost events may have included the body's delete, so absence is asked of the
                // disk here, having no event to ask. KNOWN LIMIT: an overflow landing inside
                // another program's File.Replace can find the body renamed away and empty the
                // diagram. If the replace's rename onto the body was among the lost events too,
                // it stays empty until the body next changes or the diagram is reopened.
                if (File.Exists(document.BodyPath))
                {
                    document.Reloader.Reload(document.Root.FullPath, document.BodyPath);
                }
                else
                {
                    document.Reloader.BodyDeleted(document.Root.FullPath, document.BodyPath);
                }
            }
            catch (Exception reload)
            {
                _logger.Error(reload, "Re-reading {BodyPath} after the watcher failure did not succeed", document.BodyPath);
            }
        }
    }

    public void Dispose()
    {
        lock (_subscriptionLock)
        {
            _disposed = true;
            foreach (var subscription in _subscriptions.Values)
            {
                subscription.Dispose();
            }

            _subscriptions.Clear();
        }
    }
}
