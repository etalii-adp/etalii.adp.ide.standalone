using System.Collections.Concurrent;
using EtAlii.Adp.Common;
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
            onChange: OnChange,
            onError: exception => OnWatcherError(rootPath, exception),
            includeContentChanges: true);
    }

    /// <summary>
    /// One filesystem event. A save-by-move raises Renamed with the temp name as the old path
    /// and the body as the new one; an in-place write raises Changed. A body that is gone is an
    /// empty diagram, not the last one kept alive - but a body MISSING is not yet a body gone, so
    /// the store is told which it is by the event, not by a read.
    /// </summary>
    /// <remarks>
    /// Measured with a watcher on a File.Replace save: the body is renamed away to
    /// <c>&lt;body&gt;~RF&lt;hex&gt;.TMP</c>, the scratch file is renamed onto it, and the backup is
    /// deleted - zero Deleted events for the body itself, where a real delete raises exactly one.
    /// So a store keeps its last good document through a read that fails (the body missing for an
    /// instant mid-publish) and empties it only on <see cref="IDiagramDocumentReloader.BodyDeleted"/>.
    /// <para>
    /// KNOWN LIMITS. An editor that saves by deleting the body and then creating it shows the
    /// diagram empty between its two events; the Created heals it. A rename-away to a name that is
    /// not File.Replace's backup - another tool's own safe-write scheme - is read as a delete and
    /// heals the same way when the new body arrives.
    /// </para>
    /// </remarks>
    private void OnChange(WatcherChangeTypes changeType, string? oldPath, string? newPath)
    {
        var gone = changeType switch
        {
            WatcherChangeTypes.Deleted => true,
            WatcherChangeTypes.Renamed => !IsReplaceBackup(oldPath, newPath),
            _ => false,
        };

        if (gone && oldPath is not null && _documents.TryGetValue(oldPath, out var deleted))
        {
            _logger.Debug("{BodyPath} is gone after a {ChangeType}", deleted.BodyPath, changeType);
            deleted.Reloader.BodyDeleted(deleted.RootPath, deleted.BodyPath);
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

    /// <summary>
    /// Whether a rename is File.Replace moving the body aside for the instant of a save -
    /// <c>model.dsl</c> to <c>model.dsl~RF1a2b3c.TMP</c> in the same folder - rather than the body
    /// being renamed or moved by somebody.
    /// </summary>
    private static bool IsReplaceBackup(string? oldPath, string? newPath)
    {
        if (oldPath is null || newPath is null)
        {
            return false;
        }

        var name = Path.GetFileName(newPath);
        return string.Equals(Path.GetDirectoryName(oldPath), Path.GetDirectoryName(newPath), StringComparison.OrdinalIgnoreCase)
            && name.StartsWith(Path.GetFileName(oldPath) + "~RF", StringComparison.OrdinalIgnoreCase)
            && name.EndsWith(".TMP", StringComparison.OrdinalIgnoreCase);
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
                // The lost events may have included the body's delete, so absence is asked of the
                // disk here, having no event to ask. KNOWN LIMIT: an overflow landing inside
                // another program's File.Replace can find the body renamed away and empty the
                // diagram. If the replace's rename onto the body was among the lost events too,
                // it stays empty until the body next changes or the diagram is reopened.
                if (File.Exists(document.BodyPath))
                {
                    document.Reloader.Reload(document.RootPath, document.BodyPath);
                }
                else
                {
                    document.Reloader.BodyDeleted(document.RootPath, document.BodyPath);
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
