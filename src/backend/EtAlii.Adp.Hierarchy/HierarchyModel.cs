using EtAlii.Adp.Diagram;
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

    // Guards every field below: ListEntries (gRPC threads) and the connection's own
    // FileSystemWatcher (its own background thread, via OnWatcherEvent) can call into
    // this model concurrently - plain Dictionary/HashSet are not thread-safe, so every
    // public entry point that reads or writes them takes this lock. The disk is never read
    // under it (FR-066): a folder is read, and its registrations routed, before the lock is
    // taken to apply what was read, so a slow disk holds up no other caller.
    private readonly Lock _gate = new();
    private readonly IDiagramDefinitionCatalog? _catalog;
    private readonly ToolRouter? _tools;
    private readonly DiagramStateDecorator? _states;
    private readonly HierarchyIndex _index = new();
    private readonly HashSet<ShortGuid> _listedFolderIds = new();
    private bool _rootListed;

    // Rename echoes and write transients (see WatcherEventNormalizer); used under _gate only.
    private readonly WatcherEventNormalizer _normalizer = new();

    public HierarchyModel(string rootPath, IDiagramDefinitionCatalog? catalog = null, DiagramFileRouter? router = null, EditorResolver? editorResolver = null, ToolRouter? tools = null)
    {
        RootPath = IoPath.GetFullPath(rootPath);
        _catalog = catalog;
        _tools = tools;
        _states = router is null || editorResolver is null
            ? null
            : new DiagramStateDecorator(RootPath, router, editorResolver, ResolvedBodyNameOf);
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

        Refresh? refresh;
        lock (_gate)
        {
            _normalizer.Expect(oldPath, newPath);
            refresh = OnRenamed(oldPath, newPath);
        }

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
        var facts = EventFacts.Read(heard);

        Refresh? refresh = null;
        lock (_gate)
        {
            if (_normalizer.Normalize(heard) is not { } change)
            {
                return;
            }

            switch (change.Kind)
            {
                case FileEventKind.Created when facts is not null:
                    refresh = OnCreated(change.Path, facts);
                    break;
                case FileEventKind.Deleted when facts is not null:
                    // A Deleted whose path still exists is not a deletion: Windows' ReplaceFile -
                    // which File.Replace uses, and AdpFileWriter.Save with it to rewrite a
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

                    refresh = OnRemoved(change.Path, facts);
                    break;
                case FileEventKind.Renamed when change.OldPath is { } oldPath:
                    refresh = OnRenamed(oldPath, change.Path);
                    break;
                case FileEventKind.Changed:
                    // A content change never affects the tree's shape; nothing to update.
                    break;
            }
        }

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
        EntryChanged?.Invoke(new HierarchyRootUnavailable(message));
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
        if (!IsContained(path))
        {
            fullPath = "";
            isFolder = false;
            return false;
        }

        fullPath = IoPath.GetFullPath(path);
        return true;
    }

    /// <summary>
    /// What the disk says about an event's path and its folder, read outside the lock: whether
    /// the path exists, whether it is a folder with anything in it, and whether its folder still
    /// holds anything. Null for an event that needs none of it.
    /// </summary>
    private sealed record EventFacts(bool Exists, bool IsFolder, bool HasAnyChild, bool ParentHasAnyChild)
    {
        public static EventFacts? Read(FileEvent heard)
        {
            // A rename needs none of it, unless it publishes a scratch file and so is a create.
            var needed = heard.Kind switch
            {
                FileEventKind.Created or FileEventKind.Deleted => true,
                FileEventKind.Renamed => WatcherEventNormalizer.IsTransient(heard.OldPath),
                _ => false,
            };
            if (!needed || WatcherEventNormalizer.IsTransient(heard.Path))
            {
                return null;
            }

            var isFolder = Directory.Exists(heard.Path);
            var parent = IoPath.GetDirectoryName(heard.Path);
            return new EventFacts(
                isFolder || File.Exists(heard.Path),
                isFolder,
                isFolder && SafeHasAnyChild(heard.Path),
                parent is not null && SafeHasAnyChild(parent));
        }
    }

    /// <summary>
    /// A folder whose nesting and states an event changed, with the names it held then: the
    /// registrations among them are read, and the states decided, outside the lock.
    /// </summary>
    private sealed record Refresh(string FolderPath, string? FolderName, IReadOnlyList<(string Name, bool IsFolder)> Names);

    /// <summary>
    /// What one folder's scan read from disk and decided from it, outside the lock: its entries,
    /// or the failure that made it unreadable, and where its registrations nest and what state each
    /// entry is in.
    /// </summary>
    private sealed record FolderRead(IReadOnlyList<DiskEntry>? Entries, Exception? Failure, FolderView View);

    private readonly record struct DiskEntry(string Path, string Name, bool IsFolder, bool HasAnyChild);

    /// <summary>Where one folder's registrations nest, and the state of each entry in it.</summary>
    private sealed record FolderView(IReadOnlyList<NestedEntry>? Placements, IReadOnlyList<DiagramStateDecorator.Decision> States);

    private Refresh? OnCreated(string path, EventFacts facts)
    {
        if (_index.ContainsPath(path))
        {
            return null; // already known (e.g. observed via a prior ListChildren call)
        }

        var parentPath = IoPath.GetDirectoryName(path);
        if (parentPath is null)
        {
            return null;
        }

        // A new entry under a folder can flip that folder from empty to non-empty
        // (unlocking expand) regardless of whether the folder's own children are
        // being tracked in detail on this connection - so this check runs even
        // when the create itself is about to be discarded below.
        RecomputeHasChildren(parentPath, facts.ParentHasAnyChild);

        ShortGuid? parentId;
        if (string.Equals(parentPath, RootPath, StringComparison.OrdinalIgnoreCase))
        {
            if (!_rootListed)
            {
                return null; // this connection has never listed the root (Requirement 4.7)
            }

            parentId = null;
        }
        else
        {
            if (!_index.TryGetId(parentPath, out var pid) || !_listedFolderIds.Contains(pid))
            {
                return null; // parent folder's children were never listed on this connection (Requirement 4.7)
            }

            parentId = pid;
        }

        var node = AddEntry(parentId, IoPath.GetFileName(path), facts.IsFolder, facts.HasAnyChild, path);
        EntryChanged?.Invoke(new HierarchyEntryCreated(node));

        // A created registration nests immediately, and a created subject adopts its orphans -
        // both are re-parents pushed as updates, so a client applying them in order arrives at
        // the same tree a fresh listing would build (Requirements 10.1, 10.3, 10.4). An .adp
        // appearing changes its subject's state; the update rides the same stream
        // (small-refinements Requirement 3.6).
        return RefreshOf(parentPath);
    }

    private Refresh? OnRemoved(string path, EventFacts facts)
    {
        // Mirrors OnCreated: a removal can flip a folder from non-empty to empty
        // (locking expand again), regardless of whether the removed entry itself
        // was known/tracked on this connection.
        var parentPath = IoPath.GetDirectoryName(path);
        if (parentPath is not null)
        {
            RecomputeHasChildren(parentPath, facts.ParentHasAnyChild);
        }

        if (!_index.TryGetId(path, out var id))
        {
            return null; // never known on this connection
        }

        // A removed SUBJECT must not take its nested registrations with it - their files still
        // exist. They re-parent back to the folder first, visibly orphaned rather than silently
        // gone (Requirement 8.1), and only then does the subject's own subtree go.
        if (_index.TryGet(id, out var removedEntry) && !removedEntry.IsFolder)
        {
            var folderParent = removedEntry.ParentId;
            foreach (var child in _index.ChildrenOf(id).ToList())
            {
                var reparented = child with { ParentId = folderParent };
                _index.Set(reparented);
                EntryChanged?.Invoke(new HierarchyEntryUpdated(child.Id, reparented.HasChildren, ParentChanged: true, ParentId: folderParent, DiagramState: reparented.DiagramState));
            }
        }

        RemoveSubtree(id);
        EntryChanged?.Invoke(new HierarchyEntryRemoved(id));

        // The reverse of the create: a deleted .adp downgrades its subject's state
        // (small-refinements Requirement 3.6).
        return parentPath is null ? null : RefreshOf(parentPath);
    }

    /// <summary>
    /// Updates an already-known folder's <see cref="EntryNode.HasChildren"/> to what the disk
    /// said and pushes an <see cref="HierarchyEntryUpdated"/> if it changed. A no-op for the root
    /// (it has no <see cref="EntryNode"/> of its own - the explorer's root level is always
    /// expanded) or a folder this connection doesn't know as an entry at all yet.
    /// </summary>
    private void RecomputeHasChildren(string folderPath, bool hasChildren)
    {
        if (string.Equals(folderPath, RootPath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!_index.TryGetId(folderPath, out var folderId))
        {
            return;
        }

        var entry = _index[folderId];
        if (entry.HasChildren == hasChildren)
        {
            return;
        }

        _index.Set(entry with { HasChildren = hasChildren });
        EntryChanged?.Invoke(new HierarchyEntryUpdated(folderId, hasChildren, DiagramState: entry.DiagramState));
    }

    private static bool SafeHasAnyChild(string path)
    {
        try
        {
            return Directory.EnumerateFileSystemEntries(path).Any(entry => !WatcherEventNormalizer.IsTransient(entry));
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or DirectoryNotFoundException or IOException)
        {
            return false;
        }
    }

    private Refresh? OnRenamed(string oldPath, string newPath)
    {
        if (!_index.TryGetId(oldPath, out var id))
        {
            return null; // never known on this connection
        }

        var newName = IoPath.GetFileName(newPath);
        _index.Move(id, newPath);
        _index.Set(_index[id] with { Name = newName });
        EntryChanged?.Invoke(new HierarchyEntryRenamed(id, newName));

        // A rename can change what nests where - a subject gaining or losing its name, a
        // registration changing which subject it derives.
        return IoPath.GetDirectoryName(newPath) is { } directory ? RefreshOf(directory) : null;
    }

    /// <summary>The folder's nesting and states to read again, with the names it holds now.</summary>
    private Refresh RefreshOf(string folderPath)
    {
        var folderName = !string.Equals(folderPath, RootPath, StringComparison.OrdinalIgnoreCase) && _index.TryGetId(folderPath, out var folderId)
            ? _index[folderId].Name
            : null;
        return new Refresh(folderPath, folderName, _index.In(folderPath).Select(entry => (entry.Name, entry.IsFolder)).ToList());
    }

    /// <summary>Reads a folder's nesting and states outside the lock, and applies them under it.</summary>
    private void RefreshFolder(Refresh? refresh)
    {
        if (refresh is null)
        {
            return;
        }

        var view = ReadView(refresh.FolderPath, refresh.FolderName, refresh.Names);
        lock (_gate)
        {
            ApplyNesting(refresh.FolderPath, view.Placements, raiseEvents: true);

            // The states of the root, or of a folder this connection knows; another's stay unread.
            if (string.Equals(refresh.FolderPath, RootPath, StringComparison.OrdinalIgnoreCase))
            {
                ApplyStates(null, refresh.FolderPath, view.States, raiseEvents: true);
            }
            else if (_index.TryGetId(refresh.FolderPath, out var folderId))
            {
                ApplyStates(folderId, refresh.FolderPath, view.States, raiseEvents: true);
            }
        }
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

        var read = ReadFolder(folderPath, folderName);

        lock (_gate)
        {
            // Renamed or removed while it was read: what was read describes a folder that is gone.
            if (folderId is { } id && (!_index.TryGetPath(id, out var currentPath) || !string.Equals(currentPath, folderPath, StringComparison.OrdinalIgnoreCase)))
            {
                return Array.Empty<EntryNode>();
            }

            return ApplyFolder(folderId, folderPath, read, knownBefore, raiseEvents);
        }
    }

    /// <summary>Reads a folder's entries, and decides their nesting and states, without the lock.</summary>
    private FolderRead ReadFolder(string folderPath, string? folderName)
    {
        IEnumerable<string> found;
        try
        {
            found = Directory.EnumerateFileSystemEntries(folderPath);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or DirectoryNotFoundException or IOException)
        {
            return new FolderRead(null, ex, new FolderView(null, []));
        }

        var entries = found
            .Where(entry => IsContained(entry) && !WatcherEventNormalizer.IsTransient(entry))
            .Select(entry =>
            {
                var isFolder = Directory.Exists(entry);
                return new DiskEntry(entry, IoPath.GetFileName(entry), isFolder, isFolder && SafeHasAnyChild(entry));
            })
            .ToList();
        return new FolderRead(entries, null, ReadView(folderPath, folderName, entries.Select(entry => (entry.Name, entry.IsFolder)).ToList()));
    }

    /// <summary>
    /// Where a folder's registrations nest and what state each entry is in, decided from its names
    /// without the lock: both read the registrations, and the states peek into the folders in it.
    /// </summary>
    private FolderView ReadView(string folderPath, string? folderName, IReadOnlyList<(string Name, bool IsFolder)> names) =>
        new(
            _catalog is null ? null : HierarchyNesting.Assign(names, name => ResolvedBodyNameOf(folderPath, name)),
            _states?.Decide(folderName, folderPath, names) ?? []);

    /// <summary>
    /// Applies one folder's scan under the lock: new entries are added, known ones refreshed, and
    /// those known when the scan began that it did not find are removed; then the nesting and the
    /// states read with it. An entry the watcher added while the scan ran is left alone.
    /// </summary>
    private IReadOnlyList<EntryNode> ApplyFolder(ShortGuid? folderId, string folderPath, FolderRead read, HashSet<ShortGuid> knownBefore, bool raiseEvents)
    {
        if (read.Entries is not { } diskEntries)
        {
            // The folder is shown greyed out rather than the call failing, so without this
            // line there is nothing anywhere saying why it went grey.
            _logger.Warning(read.Failure, "Could not read {FolderPath}; marking it unavailable", folderPath);
            if (folderId is { } unavailableId && _index.TryGet(unavailableId, out var unavailableEntry) && unavailableEntry.Available)
            {
                _index.Set(unavailableEntry with { Available = false });
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

        foreach (var diskEntry in diskEntries)
        {
            currentPaths.Add(diskEntry.Path);

            if (_index.TryGetId(diskEntry.Path, out var existingId))
            {
                var existing = _index[existingId];

                // A folder's HasChildren comes from disk; a FILE's comes from its nested
                // registrations, which ApplyNesting below owns - zeroing it here would make
                // every sync flap a subject's expander.
                var refreshedHasChildren = existing.IsFolder ? diskEntry.HasAnyChild : existing.HasChildren;
                if (!existing.Available || existing.HasChildren != refreshedHasChildren)
                {
                    _index.Set(existing with { Available = true, HasChildren = refreshedHasChildren });
                }
            }
            else
            {
                var node = AddEntry(folderId, diskEntry.Name, diskEntry.IsFolder, diskEntry.HasAnyChild, diskEntry.Path);

                if (raiseEvents)
                {
                    EntryChanged?.Invoke(new HierarchyEntryCreated(node));
                }
            }
        }

        // Stale detection keys on the DISK parent, not the model parent: a nested
        // registration's model parent is its subject, but it still lives in this folder,
        // and its deletion must still be noticed here.
        foreach (var staleId in _index.In(folderPath)
                     .Where(e => knownBefore.Contains(e.Id) && _index.TryGetPath(e.Id, out var p) && !currentPaths.Contains(p))
                     .Select(e => e.Id)
                     .ToList())
        {
            RemoveSubtree(staleId);
            if (raiseEvents)
            {
                EntryChanged?.Invoke(new HierarchyEntryRemoved(staleId));
            }
        }

        ApplyNesting(folderPath, read.View.Placements, raiseEvents);
        ApplyStates(folderId, folderPath, read.View.States, raiseEvents);

        // Rebuilt rather than returned from `results`: the nesting pass may have re-parented
        // entries after their snapshot was taken, and a registration appears under its subject
        // and nowhere else (Requirement 3.5) - so a folder listing carries only what still has
        // this folder as its parent.
        return _index.In(folderPath)
            .Where(e => Equals(e.ParentId, folderId))
            .OrderByDescending(e => e.IsFolder)
            .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Applies <see cref="HierarchyNesting"/>'s placement to one folder's entries: registrations
    /// re-parent under their subject files, subjects' <see cref="EntryNode.HasChildren"/> follow,
    /// and every change is pushed so a watching client converges without a reload.
    /// </summary>
    private void ApplyNesting(string folderPath, IReadOnlyList<NestedEntry>? placements, bool raiseEvents)
    {
        if (placements is null)
        {
            return;
        }

        var siblings = _index.In(folderPath).ToList();

        ShortGuid? folderParentId = null;
        if (!string.Equals(folderPath, RootPath, StringComparison.OrdinalIgnoreCase) && _index.TryGetId(folderPath, out var ownId))
        {
            folderParentId = ownId;
        }

        var byName = siblings.ToDictionary(sibling => sibling.Name, sibling => sibling, StringComparer.OrdinalIgnoreCase);
        foreach (var placement in placements)
        {
            if (!byName.TryGetValue(placement.Name, out var registration))
            {
                continue;
            }

            ShortGuid? wantedParent = placement.SubjectName is { } subjectName && byName.TryGetValue(subjectName, out var subject)
                ? subject.Id
                : folderParentId;

            var current = _index[registration.Id];
            if (!Equals(current.ParentId, wantedParent))
            {
                _index.Set(current with { ParentId = wantedParent });
                if (raiseEvents)
                {
                    EntryChanged?.Invoke(new HierarchyEntryUpdated(registration.Id, current.HasChildren, ParentChanged: true, ParentId: wantedParent, DiagramState: current.DiagramState));
                }
            }
        }

        // A subject file's HasChildren is exactly "does anything nest under it now".
        foreach (var file in siblings.Where(sibling => !sibling.IsFolder))
        {
            var current = _index[file.Id];
            var hasChildren = _index.HasChildren(file.Id);
            if (current.HasChildren != hasChildren)
            {
                _index.Set(current with { HasChildren = hasChildren });
                if (raiseEvents)
                {
                    EntryChanged?.Invoke(new HierarchyEntryUpdated(file.Id, hasChildren, DiagramState: current.DiagramState));
                }
            }
        }
    }

    /// <summary>
    /// Applies <see cref="DiagramStateDecorator"/>'s states for one scanned folder and raises
    /// <see cref="HierarchyEntryUpdated"/> where a rescan changed one. The folder's own change is
    /// raised even on a listing, whose response carries the folder's children but never the folder.
    /// </summary>
    private void ApplyStates(ShortGuid? folderId, string folderPath, IReadOnlyList<DiagramStateDecorator.Decision> states, bool raiseEvents)
    {
        var byName = new Dictionary<string, ShortGuid>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in _index.In(folderPath))
        {
            byName.TryAdd(entry.Name, entry.Id);
        }

        foreach (var decision in states)
        {
            ShortGuid entryId;
            if (decision.IsTheFolder)
            {
                if (folderId is not { } ownId || !_index.Contains(ownId))
                {
                    continue;
                }

                entryId = ownId;
            }
            else if (!byName.TryGetValue(decision.Name, out entryId))
            {
                continue; // gone while the states were read
            }

            var current = _index[entryId];
            if (current.DiagramState == decision.State)
            {
                continue;
            }

            _index.Set(current with { DiagramState = decision.State });
            if (raiseEvents || decision.IsTheFolder)
            {
                EntryChanged?.Invoke(new HierarchyEntryUpdated(entryId, current.HasChildren, DiagramState: decision.State));
            }
        }
    }

    /// <summary>
    /// The body file name a registration resolves inside its own folder, or null: no catalog,
    /// no resolution, or a body living elsewhere. Which tool's document the registration names is
    /// <see cref="ToolRouter"/>'s answer: a diagram's body, or a designer's, whose document is two
    /// files just the same and is shown as one entry the same way. Without a tool router only the
    /// diagram types are asked. Only same-folder bodies nest, since a header can point across
    /// folders and relocating an entry into another folder's subtree is not this spec.
    /// </summary>
    private string? ResolvedBodyNameOf(string folderPath, string fileName)
    {
        if (_catalog is null || !DiagramFilePair.IsRegistrationFile(fileName))
        {
            return null;
        }

        var registration = IoPath.Combine(folderPath, fileName);
        var bodyPath = _tools is null
            ? DiagramFilePair.BodyOf(registration, _catalog, RootPath)?.Path
            : _tools.Route(registration, RootPath) switch
            {
                // A type with no sibling routes to the registration itself, which is no body to nest.
                DiagramRouted { Definition.HasDocumentSibling: true } diagram => diagram.BodyPath,
                DesignerRouted designer => designer.BodyPath,
                _ => null,
            };

        if (string.IsNullOrEmpty(bodyPath))
        {
            return null;
        }

        return string.Equals(IoPath.GetDirectoryName(bodyPath), folderPath, StringComparison.OrdinalIgnoreCase)
            ? IoPath.GetFileName(bodyPath)
            : null;
    }

    private EntryNode AddEntry(ShortGuid? parentId, string name, bool isFolder, bool hasChildren, string path)
    {
        var newId = ShortGuid.NewShortGuid();
        var node = new EntryNode(newId, parentId, name, isFolder, Available: true, hasChildren);
        _index.Add(node, path);
        return node;
    }

    private void RemoveSubtree(ShortGuid id)
    {
        foreach (var removed in _index.RemoveSubtree(id))
        {
            _listedFolderIds.Remove(removed);
        }
    }

    // The link probe touches the disk, and the entry may be gone by the time it runs: a
    // multi-file delete (a git checkout, say) raises one watcher event per file, and each
    // event re-resolves every tracked selection - whose files may already have vanished
    // while their model entries have not. This runs on the watcher's thread, where an
    // unhandled exception kills the whole process (found by the diagram-workspace-tabs manual
    // pass). A gone entry is therefore not contained - it resolves to nothing, same as an
    // unknown one - which PathContainment alone would not say, since it keeps the missing part
    // of a path as written; and PathContainment answers a path it cannot probe with "not
    // inside" rather than throwing.
    private bool IsContained(string path) =>
        (File.Exists(path) || Directory.Exists(path)) && PathContainment.IsInside(RootPath, path);
}
