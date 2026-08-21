using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Backend.Hierarchy;

/// <summary>
/// One connection's private view of a project's file/folder hierarchy: entries
/// keyed by <see cref="ShortGuid"/>, populated lazily as folders are listed,
/// kept in sync with disk via <see cref="OnWatcherEvent"/> and <see cref="Reconcile"/>.
/// Never shared with another connection.
/// </summary>
public sealed class HierarchyModel
{
    // Guards every field below: ListEntries (gRPC threads) and the connection's own
    // FileSystemWatcher (its own background thread, via OnWatcherEvent) can call into
    // this model concurrently - plain Dictionary/HashSet are not thread-safe, so every
    // public entry point that reads or writes them takes this lock.
    private readonly object _gate = new();
    private readonly string _rootPath;
    private readonly Dictionary<ShortGuid, EntryNode> _entriesById = new();
    private readonly Dictionary<ShortGuid, string> _pathById = new();
    private readonly Dictionary<string, ShortGuid> _idByPath = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<ShortGuid> _listedFolderIds = new();
    private bool _rootListed;

    public HierarchyModel(string rootPath)
    {
        _rootPath = IoPath.GetFullPath(rootPath);
    }

    public event Action<HierarchyEntryChange>? EntryChanged;

    public IReadOnlyList<EntryNode> ListChildren(ShortGuid? folderId)
    {
        lock (_gate)
        {
            return SyncFolder(folderId, raiseEvents: false);
        }
    }

    /// <summary>
    /// Re-scans every folder already known to this model against current disk
    /// state, preserving ids for entries that still exist, dropping ones that
    /// no longer do, and assigning new ids to genuinely new entries — recovery
    /// for a watcher's buffer-overflow, per Requirement 4.6.
    /// </summary>
    public void Reconcile()
    {
        lock (_gate)
        {
            var knownFolderIds = _entriesById.Values.Where(e => e.IsFolder).Select(e => e.Id).ToList();

            SyncFolder(null, raiseEvents: true);
            foreach (var folderId in knownFolderIds)
            {
                if (_entriesById.ContainsKey(folderId))
                {
                    SyncFolder(folderId, raiseEvents: true);
                }
            }
        }
    }

    /// <summary>
    /// Applies one filesystem-watcher event. An event under a folder this
    /// connection has never listed is silently discarded (Requirement 4.7) —
    /// there is nothing yet to reconcile it against.
    /// </summary>
    public void OnWatcherEvent(WatcherChangeTypes changeType, string? oldPath, string? newPath)
    {
        lock (_gate)
        {
            switch (changeType)
            {
                case WatcherChangeTypes.Created when newPath is not null:
                    OnCreated(newPath);
                    break;
                case WatcherChangeTypes.Deleted when oldPath is not null:
                    OnRemoved(oldPath);
                    break;
                case WatcherChangeTypes.Renamed when oldPath is not null && newPath is not null:
                    OnRenamed(oldPath, newPath);
                    break;
                case WatcherChangeTypes.Changed:
                    // A content change never affects the tree's shape; nothing to update.
                    break;
            }
        }
    }

    public void NotifyRootUnavailable(string message) =>
        EntryChanged?.Invoke(new HierarchyEntryChange.RootUnavailable(message));

    private void OnCreated(string path)
    {
        if (_idByPath.ContainsKey(path))
        {
            return; // already known (e.g. observed via a prior ListChildren call)
        }

        var parentPath = IoPath.GetDirectoryName(path);
        if (parentPath is null)
        {
            return;
        }

        ShortGuid? parentId;
        if (string.Equals(parentPath, _rootPath, StringComparison.OrdinalIgnoreCase))
        {
            if (!_rootListed)
            {
                return; // this connection has never listed the root (Requirement 4.7)
            }

            parentId = null;
        }
        else
        {
            if (!_idByPath.TryGetValue(parentPath, out var pid) || !_listedFolderIds.Contains(pid))
            {
                return; // parent folder's children were never listed on this connection (Requirement 4.7)
            }

            parentId = pid;
        }

        var isFolder = Directory.Exists(path);
        var node = AddEntry(parentId, IoPath.GetFileName(path), isFolder, path);
        EntryChanged?.Invoke(new HierarchyEntryChange.Created(node));
    }

    private void OnRemoved(string path)
    {
        if (!_idByPath.TryGetValue(path, out var id))
        {
            return; // never known on this connection
        }

        RemoveSubtree(id);
        EntryChanged?.Invoke(new HierarchyEntryChange.Removed(id));
    }

    private void OnRenamed(string oldPath, string newPath)
    {
        if (!_idByPath.TryGetValue(oldPath, out var id))
        {
            return; // never known on this connection
        }

        var newName = IoPath.GetFileName(newPath);
        RenumberPath(id, oldPath, newPath);
        _entriesById[id] = _entriesById[id] with { Name = newName };
        EntryChanged?.Invoke(new HierarchyEntryChange.Renamed(id, newName));
    }

    private IReadOnlyList<EntryNode> SyncFolder(ShortGuid? folderId, bool raiseEvents)
    {
        string folderPath;
        if (folderId is { } id)
        {
            if (!_pathById.TryGetValue(id, out var knownPath))
            {
                return Array.Empty<EntryNode>(); // unknown folder id
            }

            folderPath = knownPath;
        }
        else
        {
            folderPath = _rootPath;
        }

        IEnumerable<string> diskEntries;
        try
        {
            diskEntries = Directory.EnumerateFileSystemEntries(folderPath).Where(IsContained);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or DirectoryNotFoundException or IOException)
        {
            if (folderId is { } unavailableId && _entriesById.TryGetValue(unavailableId, out var unavailableEntry) && unavailableEntry.Available)
            {
                _entriesById[unavailableId] = unavailableEntry with { Available = false };
            }

            return Array.Empty<EntryNode>();
        }

        if (folderId is { } listedId)
        {
            _listedFolderIds.Add(listedId);
        }
        else
        {
            _rootListed = true;
        }

        var currentPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var results = new List<EntryNode>();

        foreach (var entryPath in diskEntries)
        {
            currentPaths.Add(entryPath);

            if (_idByPath.TryGetValue(entryPath, out var existingId))
            {
                var existing = _entriesById[existingId];
                if (!existing.Available)
                {
                    existing = existing with { Available = true };
                    _entriesById[existingId] = existing;
                }

                results.Add(existing);
            }
            else
            {
                var isFolder = Directory.Exists(entryPath);
                var node = AddEntry(folderId, IoPath.GetFileName(entryPath), isFolder, entryPath);
                results.Add(node);

                if (raiseEvents)
                {
                    EntryChanged?.Invoke(new HierarchyEntryChange.Created(node));
                }
            }
        }

        foreach (var staleId in _entriesById.Values
                     .Where(e => Equals(e.ParentId, folderId) && _pathById.TryGetValue(e.Id, out var p) && !currentPaths.Contains(p))
                     .Select(e => e.Id)
                     .ToList())
        {
            RemoveSubtree(staleId);
            if (raiseEvents)
            {
                EntryChanged?.Invoke(new HierarchyEntryChange.Removed(staleId));
            }
        }

        return results
            .OrderByDescending(e => e.IsFolder)
            .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private EntryNode AddEntry(ShortGuid? parentId, string name, bool isFolder, string path)
    {
        var newId = ShortGuid.NewShortGuid();
        var node = new EntryNode(newId, parentId, name, isFolder, Available: true);
        _entriesById[newId] = node;
        _pathById[newId] = path;
        _idByPath[path] = newId;
        return node;
    }

    private void RemoveSubtree(ShortGuid id)
    {
        foreach (var childId in _entriesById.Values.Where(e => e.ParentId == id).Select(e => e.Id).ToList())
        {
            RemoveSubtree(childId);
        }

        if (_pathById.Remove(id, out var path))
        {
            _idByPath.Remove(path);
        }

        _entriesById.Remove(id);
        _listedFolderIds.Remove(id);
    }

    private void RenumberPath(ShortGuid id, string oldPath, string newPath)
    {
        foreach (var childId in _entriesById.Values.Where(e => e.ParentId == id).Select(e => e.Id).ToList())
        {
            var childOldPath = _pathById[childId];
            var childNewPath = IoPath.Combine(newPath, IoPath.GetFileName(childOldPath));
            RenumberPath(childId, childOldPath, childNewPath);
        }

        _idByPath.Remove(oldPath);
        _idByPath[newPath] = id;
        _pathById[id] = newPath;
    }

    private bool IsContained(string path)
    {
        var fullPath = IoPath.GetFullPath(path);
        var normalizedRoot = _rootPath.EndsWith(IoPath.DirectorySeparatorChar)
            ? _rootPath
            : _rootPath + IoPath.DirectorySeparatorChar;

        if (!fullPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var linkTarget = File.ResolveLinkTarget(fullPath, returnFinalTarget: true)?.FullName;
        return linkTarget is null || linkTarget.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
    }
}
