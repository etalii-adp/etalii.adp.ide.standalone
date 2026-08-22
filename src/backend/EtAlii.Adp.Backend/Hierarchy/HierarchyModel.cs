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
        // ADP's own scratch files are not project content: AdpFileWriter writes a new diagram
        // to one of these in the destination folder and moves it into place, so ignoring them
        // here is what keeps that write from flickering through every connected explorer.
        if (IsScratchFile(oldPath) || IsScratchFile(newPath))
        {
            return;
        }

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

    /// <summary>
    /// Whether a path is one of <see cref="AdpFileWriter"/>'s temporary files. A reconcile
    /// filters on this too, so a scratch file left behind by an interrupted write can never
    /// reappear as an entry.
    /// </summary>
    private static bool IsScratchFile(string? path)
    {
        if (path is null)
        {
            return false;
        }

        var name = IoPath.GetFileName(path);
        return name.StartsWith(AdpFileWriter.TempPrefix, StringComparison.Ordinal) &&
            name.EndsWith(AdpFileWriter.TempExtension, StringComparison.OrdinalIgnoreCase);
    }

    public void NotifyRootUnavailable(string message) =>
        EntryChanged?.Invoke(new HierarchyEntryChange.RootUnavailable(message));

    /// <summary>
    /// Resolves an entry id this connection knows to where it currently lives on disk.
    /// Containment is re-checked here rather than trusted from listing time, so an entry
    /// whose path has since been made to point outside the root (e.g. a swapped symlink)
    /// resolves to nothing. An id from another connection or project is unknown here and
    /// resolves to nothing too, which is what keeps it from leaking its own existence.
    /// </summary>
    public bool TryResolvePath(ShortGuid entryId, out string fullPath, out bool isFolder)
    {
        lock (_gate)
        {
            if (!_pathById.TryGetValue(entryId, out var path) || !IsContained(path))
            {
                fullPath = "";
                isFolder = false;
                return false;
            }

            fullPath = IoPath.GetFullPath(path);
            isFolder = _entriesById[entryId].IsFolder;
            return true;
        }
    }

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

        // A new entry under a folder can flip that folder from empty to non-empty
        // (unlocking expand) regardless of whether the folder's own children are
        // being tracked in detail on this connection - so this check runs even
        // when the create itself is about to be discarded below.
        RecomputeHasChildren(parentPath);

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
        // Mirrors OnCreated: a removal can flip a folder from non-empty to empty
        // (locking expand again), regardless of whether the removed entry itself
        // was known/tracked on this connection.
        var parentPath = IoPath.GetDirectoryName(path);
        if (parentPath is not null)
        {
            RecomputeHasChildren(parentPath);
        }

        if (!_idByPath.TryGetValue(path, out var id))
        {
            return; // never known on this connection
        }

        RemoveSubtree(id);
        EntryChanged?.Invoke(new HierarchyEntryChange.Removed(id));
    }

    /// <summary>
    /// Refreshes an already-known folder's <see cref="EntryNode.HasChildren"/> against
    /// current disk state and pushes an <see cref="HierarchyEntryChange.Updated"/> if it
    /// changed. A no-op for the root (it has no <see cref="EntryNode"/> of its own - the
    /// explorer's root level is always expanded) or a folder this connection doesn't know
    /// as an entry at all yet.
    /// </summary>
    private void RecomputeHasChildren(string folderPath)
    {
        if (string.Equals(folderPath, _rootPath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!_idByPath.TryGetValue(folderPath, out var folderId))
        {
            return;
        }

        var hasChildren = SafeHasAnyChild(folderPath);
        var entry = _entriesById[folderId];
        if (entry.HasChildren == hasChildren)
        {
            return;
        }

        _entriesById[folderId] = entry with { HasChildren = hasChildren };
        EntryChanged?.Invoke(new HierarchyEntryChange.Updated(folderId, hasChildren));
    }

    private static bool SafeHasAnyChild(string path)
    {
        try
        {
            return Directory.EnumerateFileSystemEntries(path).Any(entry => !IsScratchFile(entry));
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or DirectoryNotFoundException or IOException)
        {
            return false;
        }
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
            diskEntries = Directory.EnumerateFileSystemEntries(folderPath).Where(entry => IsContained(entry) && !IsScratchFile(entry));
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
                var refreshedHasChildren = existing.IsFolder && SafeHasAnyChild(entryPath);
                if (!existing.Available || existing.HasChildren != refreshedHasChildren)
                {
                    existing = existing with { Available = true, HasChildren = refreshedHasChildren };
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
        var hasChildren = isFolder && SafeHasAnyChild(path);
        var node = new EntryNode(newId, parentId, name, isFolder, Available: true, hasChildren);
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
