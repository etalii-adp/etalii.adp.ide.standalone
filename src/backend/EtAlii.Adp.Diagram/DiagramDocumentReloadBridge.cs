using System.Collections.Concurrent;
using EtAlii.Adp.Common;
using EtAlii.Adp.Diagram;
using EtAlii.Adp.Hierarchy;
using Serilog;

namespace EtAlii.Adp.Diagram;

/// <summary>
/// The watcher-to-store bridge: the one place a disk change reaches a diagram document store's
/// <c>Reload</c>. <c>DiagramService.Open</c> reports every diagram it opens; this keeps one
/// content-watching <see cref="RootFolderWatcher"/> per project root and, when a tracked body
/// or registration file changes underneath, hands the owning module's
/// <see cref="IDiagramDocumentReloader"/> the body to re-read. The stores push the result to
/// their sessions themselves - the bridge only tells them the disk moved (modular-text-editors
/// Requirements 5.3, 5.5).
/// </summary>
/// <remarks>
/// Tracked paths and watchers live for the process, deliberately outliving the streams that
/// opened them: the stores cache documents by path for the process too, so an external write
/// landing while no session is open would otherwise leave a closed-and-reopened diagram on the
/// stale pre-change model until a restart.
/// </remarks>
public sealed class DiagramDocumentReloadBridge : IDisposable
{
    private static readonly ILogger _logger = Log.ForContext<DiagramDocumentReloadBridge>();

    private readonly IReadOnlyDictionary<DiagramOrigin, IDiagramDocumentReloader> _reloaders;
    private readonly ConcurrentDictionary<string, TrackedDocument> _documents = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, string> _registrations = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, RootFolderWatcher> _watchers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _watcherLock = new();
    private bool _disposed;

    private sealed record TrackedDocument(string RootPath, string BodyPath, IDiagramDocumentReloader Reloader);

    public DiagramDocumentReloadBridge(IEnumerable<IDiagramDocumentReloader> reloaders)
    {
        ArgumentNullException.ThrowIfNull(reloaders);
        _reloaders = reloaders.ToDictionary(reloader => reloader.Origin);
    }

    /// <summary>
    /// Starts caring about one opened diagram: its body, the <c>.adp</c> it was opened through
    /// (when there is one), and the root to watch them under. A type that registered no
    /// reloader is left alone - the ansible module watches its own folder already.
    /// </summary>
    public void Track(string rootPath, string bodyPath, string? registrationPath, DiagramOrigin origin)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(bodyPath);
        ArgumentNullException.ThrowIfNull(origin);

        if (!_reloaders.TryGetValue(origin, out var reloader))
        {
            return;
        }

        _documents[bodyPath] = new TrackedDocument(rootPath, bodyPath, reloader);
        if (registrationPath is { Length: > 0 })
        {
            _registrations[registrationPath] = bodyPath;
        }

        EnsureWatching(rootPath);
    }

    private void EnsureWatching(string rootPath)
    {
        if (_watchers.ContainsKey(rootPath))
        {
            return;
        }

        // Under a lock rather than GetOrAdd: two Opens racing here would each construct a live
        // FileSystemWatcher, and GetOrAdd disposes neither loser.
        lock (_watcherLock)
        {
            if (_disposed || _watchers.ContainsKey(rootPath))
            {
                return;
            }

            try
            {
                _watchers[rootPath] = CreateWatcher(rootPath);
            }
            catch (Exception exception) when (exception is ArgumentException or IOException)
            {
                // A root that cannot be watched - deleted underneath, an inaccessible share -
                // degrades to the old behavior for that project: reloads on restart only.
                _logger.Warning(exception, "Cannot watch {RootPath}; external edits there will not reach open diagrams", rootPath);
            }
        }
    }

    private RootFolderWatcher CreateWatcher(string rootPath)
    {
        return new RootFolderWatcher(
            rootPath,
            onChange: (_, oldPath, newPath) => OnChange(oldPath, newPath),
            onError: exception => OnWatcherError(rootPath, exception),
            includeContentChanges: true);
    }

    /// <summary>
    /// One filesystem event. A save-by-move raises Renamed with the temp name as the old path
    /// and the body as the new one; an in-place write raises Changed; a delete still reloads,
    /// because a body that is gone is an empty diagram, not the last one kept alive.
    /// </summary>
    private void OnChange(string? oldPath, string? newPath)
    {
        ReloadIfTracked(oldPath);
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
        document.Reloader.Reload(document.RootPath, document.BodyPath);
    }

    /// <summary>
    /// A broken watcher lost events - a buffer overflow above all - so the stale answers it
    /// allowed are corrected the blunt way: recreate it and re-read every tracked document
    /// under its root. Reload tells the sessions, so nothing stays quietly behind the disk.
    /// </summary>
    private void OnWatcherError(string rootPath, Exception exception)
    {
        _logger.Warning(exception, "The watcher on {RootPath} failed; recreating it and re-reading every tracked diagram there", rootPath);

        lock (_watcherLock)
        {
            if (_disposed || !_watchers.TryRemove(rootPath, out var broken))
            {
                return;
            }

            broken.Dispose();
            try
            {
                _watchers[rootPath] = CreateWatcher(rootPath);
            }
            catch (Exception recreate) when (recreate is ArgumentException or IOException)
            {
                _logger.Warning(recreate, "Cannot re-watch {RootPath}; external edits there will not reach open diagrams", rootPath);
                return;
            }
        }

        foreach (var document in _documents.Values.Where(candidate => string.Equals(candidate.RootPath, rootPath, StringComparison.OrdinalIgnoreCase)))
        {
            // Guarded one by one: this runs on the watcher's thread, where a document that no
            // longer parses must cost its own reload and nothing else's - and certainly not
            // the process.
            try
            {
                document.Reloader.Reload(document.RootPath, document.BodyPath);
            }
            catch (Exception reload)
            {
                _logger.Error(reload, "Re-reading {BodyPath} after the watcher failure did not succeed", document.BodyPath);
            }
        }
    }

    public void Dispose()
    {
        lock (_watcherLock)
        {
            _disposed = true;
            foreach (var watcher in _watchers.Values)
            {
                watcher.Dispose();
            }

            _watchers.Clear();
        }
    }
}
