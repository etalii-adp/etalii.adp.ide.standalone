using EtAlii.Adp.Files;
using Serilog;
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Hierarchy;

/// <summary>
/// One connection's private view of a project's file/folder hierarchy: entries
/// keyed by <see cref="ShortGuid"/>, populated lazily as folders are listed,
/// kept in sync with disk via <see cref="OnWatcherEvent"/> and <see cref="Reconcile"/>.
/// Never shared with another connection.
/// </summary>
public sealed class HierarchyModel
{
    private static readonly ILogger _logger = Log.ForContext<HierarchyModel>();

    // Guards the index, the tree and every field after them: ListEntries (gRPC threads) and
    // the connection's own FileSystemWatcher (its own background thread, via OnWatcherEvent)
    // can call into this model concurrently - plain Dictionary/HashSet are not thread-safe, so
    // every public entry point that reads or writes them takes this lock. The disk is never read
    // under it (FR-066): a folder is read, and its registrations routed, before the lock is
    // taken to apply what was read, so a slow disk holds up no other caller.
    private readonly Lock _gate = new();
    private readonly HierarchyIndex _index = new();

    // What the disk says, read outside _gate; and the changes it makes, applied under _gate only.
    private readonly HierarchyFolderReader _reader;
    private readonly HierarchyTree _tree;

    // Rename echoes and write transients (see WatcherEventNormalizer); used under _gate only.
    private readonly WatcherEventNormalizer _normalizer = new();

    // The changes applied under _gate and not yet raised, and whether a thread is raising them.
    // A change is raised after the lock is released (FR-026), so a subscriber that waits on
    // another thread asking the model something cannot deadlock the two (report D10).
    private readonly List<HierarchyEntryChange> _queued = new();
    private bool _raising;

    public HierarchyModel(string rootPath, ToolRouter tools)
    {
        ArgumentNullException.ThrowIfNull(tools);

        RootPath = IoPath.GetFullPath(rootPath);
        _reader = new HierarchyFolderReader(RootPath, tools);
        _tree = new HierarchyTree(RootPath, _index, Queue);
    }

    /// <summary>The absolute project root this model views; what the store matches on to route a rename to it.</summary>
    public string RootPath { get; }

    public event Action<HierarchyEntryChange>? EntryChanged;

    /// <summary>
    /// Applies a rename this connection's own command just performed on disk, deterministically
    /// and on every platform - a stable-id <see cref="HierarchyEntryRenamed"/>, not the
    /// Remove+Create the Linux watcher would otherwise produce. The watcher's later echo of the
    /// same move is suppressed. Idempotent: an old path this model never listed is a no-op.
    /// </summary>
    public void ApplyLocalRename(string oldPath, string newPath)
    {
        ArgumentNullException.ThrowIfNull(oldPath);
        ArgumentNullException.ThrowIfNull(newPath);

        HierarchyTree.Refresh? refresh;
        lock (_gate)
        {
            _normalizer.Expect(oldPath, newPath);
            refresh = _tree.OnRenamed(oldPath, newPath);
        }

        RaiseQueued();
        RefreshFolder(refresh);
    }

    /// <summary>
    /// Announces a rename a command is about to perform, before it touches the disk, so the
    /// watcher's echo is recognised even when it outruns the command: the watcher thread can
    /// see the move before <see cref="ApplyLocalRename"/> runs, and on Linux an unrecognised
    /// echo lands as a Remove+Create that discards the entry's id and never raises the Renamed
    /// (seen as an intermittent CI failure of UndoRedoFlowTests). Withdrawn with
    /// <see cref="WithdrawLocalRename"/> when the move does not happen.
    /// </summary>
    public void ExpectLocalRename(string oldPath, string newPath)
    {
        ArgumentNullException.ThrowIfNull(oldPath);
        ArgumentNullException.ThrowIfNull(newPath);

        lock (_gate)
        {
            _normalizer.Expect(oldPath, newPath);
        }
    }

    /// <summary>
    /// Forgets a rename announced by <see cref="ExpectLocalRename"/> that did not take place, so
    /// a genuine delete of the old path is not mistaken for its echo.
    /// </summary>
    public void WithdrawLocalRename(string oldPath, string newPath)
    {
        ArgumentNullException.ThrowIfNull(oldPath);
        ArgumentNullException.ThrowIfNull(newPath);

        lock (_gate)
        {
            _normalizer.Withdraw(oldPath, newPath);
        }
    }

    public IReadOnlyList<EntryNode> ListChildren(ShortGuid? folderId)
    {
        ShortGuid? fileId = null;
        lock (_gate)
        {
            if (folderId is { } id && _index.TryGet(id, out var entry) && !entry.IsFolder)
            {
                // Listing a FILE: its children are its nested registrations (adp-file-nesting
                // Requirement 3). Syncing the containing folder is what nests them.
                fileId = id;
                folderId = entry.ParentId;
            }
        }

        var listing = Sync(folderId, raiseEvents: false);
        if (fileId is not { } file)
        {
            return listing;
        }

        lock (_gate)
        {
            return _index.ChildrenOf(file)
                .OrderBy(e => e.Name, Comparer<string>.Create(HierarchyNesting.CompareRegistrations))
                .ToList();
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
        List<ShortGuid> knownFolderIds;
        lock (_gate)
        {
            knownFolderIds = _index.Folders.Select(e => e.Id).ToList();
        }

        // Debug rather than Information: a reconcile is recovery, but its trigger is
        // already logged where it happened, and this only says how much was re-scanned.
        _logger.Debug(
            "Reconciling {RootPath} against disk: the root plus {FolderCount} known folders",
            RootPath,
            knownFolderIds.Count);

        // A folder the sync of another removed is unknown by its turn, and its sync reads nothing.
        Sync(null, raiseEvents: true);
        foreach (var folderId in knownFolderIds)
        {
            Sync(folderId, raiseEvents: true);
        }
    }

    /// <summary>
    /// Applies one event of the project's <see cref="ProjectFileEvents"/>, as
    /// <see cref="WatcherEventNormalizer"/> reads it. An event under a folder this connection has
    /// never listed is silently discarded (Requirement 4.7) - there is nothing yet to reconcile it
    /// against. A Lost is not the model's to answer: the service decides whether the tree is read
    /// again or the root is gone.
    /// </summary>
    public void OnFileEvent(FileEvent heard)
    {
        // What the event's path is on disk, read before the lock is taken.
        var facts = HierarchyFolderReader.FactsOf(heard);

        HierarchyTree.Refresh? refresh = null;
        lock (_gate)
        {
            if (_normalizer.Normalize(heard) is not { } change)
            {
                return;
            }

            switch (change.Kind)
            {
                case FileEventKind.Created when facts is not null:
                    refresh = _tree.OnCreated(change.Path, facts);
                    break;
                case FileEventKind.Deleted when facts is not null:
                    // A Deleted whose path still exists is not a deletion: Windows' ReplaceFile -
                    // which File.Replace uses, and FileWriter.SaveAsync with it to rewrite a
                    // registration in place - raises a spurious Deleted for the destination it
                    // just refreshed, immediately followed by a Created. Removing the entry here
                    // would hand the file a new id on that Created, orphaning any open document
                    // that still holds the old one. An entry stands for a path, and the path is
                    // still there, so the entry - and its id - must survive. A genuine deletion
                    // leaves nothing on disk and still removes, as it must.
                    if (facts.Exists)
                    {
                        _logger.Verbose("Dropping a Deleted watcher event for {Path}, which still exists on disk - an in-place replace, not a deletion", change.Path);
                        break;
                    }

                    refresh = _tree.OnRemoved(change.Path, facts);
                    break;
                case FileEventKind.Renamed when change.OldPath is { } oldPath:
                    refresh = _tree.OnRenamed(oldPath, change.Path);
                    break;
                case FileEventKind.Changed:
                    // A content change never affects the tree's shape; nothing to update.
                    break;
            }
        }

        RaiseQueued();
        RefreshFolder(refresh);
    }

    /// <summary>Applies one event in the watcher's own shape; see <see cref="OnFileEvent"/>.</summary>
    public void OnWatcherEvent(WatcherChangeTypes changeType, string? oldPath, string? newPath)
    {
        if (WatcherEventNormalizer.Translate(changeType, oldPath, newPath) is { } heard)
        {
            OnFileEvent(heard);
        }
    }

    public void NotifyRootUnavailable(string message)
    {
        _logger.Warning("Telling the client that {RootPath} is unavailable: {Reason}", RootPath, message);
        lock (_gate)
        {
            Queue(new HierarchyRootUnavailable(message));
        }

        RaiseQueued();
    }

    /// <summary>
    /// Resolves an entry id this connection knows to where it currently lives on disk.
    /// Containment is re-checked here rather than trusted from listing time, so an entry
    /// whose path has since been made to point outside the root (e.g. a swapped symlink)
    /// resolves to nothing. An id from another connection or project is unknown here and
    /// resolves to nothing too, which is what keeps it from leaking its own existence.
    /// </summary>
    public bool TryResolvePath(ShortGuid entryId, out string fullPath, out bool isFolder)
    {
        string path;
        lock (_gate)
        {
            if (!_index.TryGetPath(entryId, out path))
            {
                fullPath = "";
                isFolder = false;
                return false;
            }

            isFolder = _index[entryId].IsFolder;
        }

        // The containment check probes the disk, so it runs after the lock is released.
        if (!_reader.IsContained(path))
        {
            fullPath = "";
            isFolder = false;
            return false;
        }

        fullPath = IoPath.GetFullPath(path);
        return true;
    }

    /// <summary>Queues a change applied under the lock, for <see cref="RaiseQueued"/> to raise after it.</summary>
    private void Queue(HierarchyEntryChange change) => _queued.Add(change);

    /// <summary>
    /// Raises the queued changes, in the order they were applied, with the lock released. One
    /// thread raises at a time: a thread finding another raising leaves its changes to that one,
    /// which raises until the queue is empty, so a subscriber hears every change once and in
    /// order, and one that calls back into the model hears that call's changes after the current
    /// ones rather than inside them.
    /// </summary>
    private void RaiseQueued()
    {
        while (true)
        {
            HierarchyEntryChange[] changes;
            lock (_gate)
            {
                if (_raising || _queued.Count == 0)
                {
                    return;
                }

                _raising = true;
                changes = [.. _queued];
                _queued.Clear();
            }

            try
            {
                foreach (var change in changes)
                {
                    EntryChanged?.Invoke(change);
                }
            }
            finally
            {
                lock (_gate)
                {
                    _raising = false;
                }
            }
        }
    }

    /// <summary>Reads a folder's nesting and states outside the lock, and applies them under it.</summary>
    private void RefreshFolder(HierarchyTree.Refresh? refresh)
    {
        if (refresh is null)
        {
            return;
        }

        var view = _reader.ReadView(refresh.FolderPath, refresh.FolderName, refresh.Names);
        lock (_gate)
        {
            _tree.ApplyRefresh(refresh, view);
        }

        RaiseQueued();
    }

    /// <summary>
    /// Brings one folder's entries in line with the disk: its path is taken under the lock, the
    /// disk is read without it, and what was read is applied under it again. Returns the folder's
    /// listing.
    /// </summary>
    private IReadOnlyList<EntryNode> Sync(ShortGuid? folderId, bool raiseEvents)
    {
        string folderPath;
        string? folderName = null;
        HashSet<ShortGuid> knownBefore;
        lock (_gate)
        {
            if (folderId is { } id)
            {
                if (!_index.TryGetPath(id, out var knownPath))
                {
                    return Array.Empty<EntryNode>(); // unknown folder id
                }

                folderPath = knownPath;
                folderName = _index[id].Name;
            }
            else
            {
                folderPath = RootPath;
            }

            knownBefore = _index.In(folderPath).Select(entry => entry.Id).ToHashSet();
        }

        var read = _reader.Read(folderPath, folderName);

        IReadOnlyList<EntryNode> listing;
        lock (_gate)
        {
            // Renamed or removed while it was read: what was read describes a folder that is gone.
            listing = folderId is { } id && (!_index.TryGetPath(id, out var currentPath) || !string.Equals(currentPath, folderPath, StringComparison.OrdinalIgnoreCase))
                ? Array.Empty<EntryNode>()
                : _tree.ApplyFolder(folderId, folderPath, read, knownBefore, raiseEvents);
        }

        RaiseQueued();
        return listing;
    }
}
