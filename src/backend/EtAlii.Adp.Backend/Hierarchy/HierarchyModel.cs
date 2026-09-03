using EtAlii.Adp.Diagram;
using Serilog;
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
    private static readonly ILogger _logger = Log.ForContext<HierarchyModel>();

    private readonly Lock _gate = new();
    private readonly string _rootPath;
    private readonly IDiagramDefinitionCatalog? _catalog;
    private readonly DiagramFileRouter? _router;
    private readonly EditorResolver? _editorResolver;
    private readonly Dictionary<ShortGuid, EntryNode> _entriesById = new();
    private readonly Dictionary<ShortGuid, string> _pathById = new();
    private readonly Dictionary<string, ShortGuid> _idByPath = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<ShortGuid> _listedFolderIds = new();
    private bool _rootListed;

    public HierarchyModel(string rootPath, IDiagramDefinitionCatalog? catalog = null, DiagramFileRouter? router = null, EditorResolver? editorResolver = null)
    {
        _rootPath = IoPath.GetFullPath(rootPath);
        _catalog = catalog;
        _router = router;
        _editorResolver = editorResolver;
    }

    public event Action<HierarchyEntryChange>? EntryChanged;

    public IReadOnlyList<EntryNode> ListChildren(ShortGuid? folderId)
    {
        lock (_gate)
        {
            if (folderId is { } id && _entriesById.TryGetValue(id, out var entry) && !entry.IsFolder)
            {
                // Listing a FILE: its children are its nested registrations (adp-file-nesting
                // Requirement 3). Syncing the containing folder is what nests them.
                SyncFolder(entry.ParentId, raiseEvents: false);
                return _entriesById.Values
                    .Where(e => e.ParentId == id)
                    .OrderBy(e => e.Name, Comparer<string>.Create(HierarchyNesting.CompareRegistrations))
                    .ToList();
            }

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

            // Debug rather than Information: a reconcile is recovery, but its trigger is
            // already logged where it happened, and this only says how much was re-scanned.
            _logger.Debug(
                "Reconciling {RootPath} against disk: the root plus {FolderCount} known folders",
                _rootPath,
                knownFolderIds.Count);

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
        // to one of these in the destination folder and moves it into place. The scratch file
        // itself never becomes an entry - but the move that publishes the diagram is reported
        // as a rename *away* from that name, and it is the only announcement the real file
        // gets, so it is turned into the create it actually is.
        if (IsScratchFile(newPath))
        {
            _logger.Verbose("Ignoring the watcher event for scratch file {Path}", newPath);
            return;
        }

        if (IsScratchFile(oldPath))
        {
            if (changeType == WatcherChangeTypes.Renamed && newPath is not null)
            {
                _logger.Verbose("Reading the move of scratch file {OldPath} onto {NewPath} as a create", oldPath, newPath);
                changeType = WatcherChangeTypes.Created;
                oldPath = null;
            }
            else
            {
                _logger.Verbose("Ignoring the {ChangeType} watcher event for scratch file {Path}", changeType, oldPath);
                return;
            }
        }

        // Verbose: one line per file system event, which is far too much for normal running
        // but exactly what is wanted when the tree and disk have gone out of step.
        _logger.Verbose("Watcher event {ChangeType}: {OldPath} -> {NewPath}", changeType, oldPath, newPath);

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

    public void NotifyRootUnavailable(string message)
    {
        _logger.Warning("Telling the client that {RootPath} is unavailable: {Reason}", _rootPath, message);
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
        EntryChanged?.Invoke(new HierarchyEntryCreated(node));

        // A created registration nests immediately, and a created subject adopts its orphans -
        // both are re-parents pushed as updates, so a client applying them in order arrives at
        // the same tree a fresh listing would build (Requirements 10.1, 10.3, 10.4).
        ApplyNesting(parentPath, raiseEvents: true);

        // An .adp appearing changes its subject's state; the update rides the same stream
        // (small-refinements Requirement 3.6).
        RefreshDiagramStatesAt(parentPath, raiseEvents: true);
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

        // A removed SUBJECT must not take its nested registrations with it - their files still
        // exist. They re-parent back to the folder first, visibly orphaned rather than silently
        // gone (Requirement 8.1), and only then does the subject's own subtree go.
        if (_entriesById.TryGetValue(id, out var removedEntry) && !removedEntry.IsFolder)
        {
            var folderParent = removedEntry.ParentId;
            foreach (var childId in _entriesById.Values.Where(e => e.ParentId == id).Select(e => e.Id).ToList())
            {
                _entriesById[childId] = _entriesById[childId] with { ParentId = folderParent };
                EntryChanged?.Invoke(new HierarchyEntryUpdated(childId, _entriesById[childId].HasChildren, ParentChanged: true, ParentId: folderParent, DiagramState: _entriesById[childId].DiagramState));
            }
        }

        RemoveSubtree(id);
        EntryChanged?.Invoke(new HierarchyEntryRemoved(id));

        if (parentPath is not null)
        {
            ApplyNesting(parentPath, raiseEvents: true);

            // The reverse of the create: a deleted .adp downgrades its subject's state
            // (small-refinements Requirement 3.6).
            RefreshDiagramStatesAt(parentPath, raiseEvents: true);
        }
    }

    /// <summary>
    /// Refreshes an already-known folder's <see cref="EntryNode.HasChildren"/> against
    /// current disk state and pushes an <see cref="HierarchyEntryUpdated"/> if it
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
        EntryChanged?.Invoke(new HierarchyEntryUpdated(folderId, hasChildren, DiagramState: entry.DiagramState));
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
        EntryChanged?.Invoke(new HierarchyEntryRenamed(id, newName));

        // A rename can change what nests where - a subject gaining or losing its name, a
        // registration changing which subject it derives.
        if (IoPath.GetDirectoryName(newPath) is { } directory)
        {
            ApplyNesting(directory, raiseEvents: true);
            RefreshDiagramStatesAt(directory, raiseEvents: true);
        }
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
            // The folder is shown greyed out rather than the call failing, so without this
            // line there is nothing anywhere saying why it went grey.
            _logger.Warning(ex, "Could not read {FolderPath}; marking it unavailable", folderPath);
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
        //var results = new List<EntryNode>();

        foreach (var entryPath in diskEntries)
        {
            currentPaths.Add(entryPath);

            if (_idByPath.TryGetValue(entryPath, out var existingId))
            {
                var existing = _entriesById[existingId];

                // A folder's HasChildren comes from disk; a FILE's comes from its nested
                // registrations, which ApplyNesting below owns - zeroing it here would make
                // every sync flap a subject's expander.
                var refreshedHasChildren = existing.IsFolder ? SafeHasAnyChild(entryPath) : existing.HasChildren;
                if (!existing.Available || existing.HasChildren != refreshedHasChildren)
                {
                    existing = existing with { Available = true, HasChildren = refreshedHasChildren };
                    _entriesById[existingId] = existing;
                }

                //results.Add(existing);
            }
            else
            {
                var isFolder = Directory.Exists(entryPath);
                var node = AddEntry(folderId, IoPath.GetFileName(entryPath), isFolder, entryPath);
                //results.Add(node);

                if (raiseEvents)
                {
                    EntryChanged?.Invoke(new HierarchyEntryCreated(node));
                }
            }
        }

        // Stale detection keys on the DISK parent, not the model parent: a nested
        // registration's model parent is its subject, but it still lives in this folder,
        // and its deletion must still be noticed here.
        foreach (var staleId in _entriesById.Values
                     .Where(e => _pathById.TryGetValue(e.Id, out var p)
                                 && string.Equals(IoPath.GetDirectoryName(p), folderPath, StringComparison.OrdinalIgnoreCase)
                                 && !currentPaths.Contains(p))
                     .Select(e => e.Id)
                     .ToList())
        {
            RemoveSubtree(staleId);
            if (raiseEvents)
            {
                EntryChanged?.Invoke(new HierarchyEntryRemoved(staleId));
            }
        }

        ApplyNesting(folderPath, raiseEvents);
        RefreshDiagramStates(folderId, folderPath, raiseEvents);

        // Rebuilt rather than returned from `results`: the nesting pass may have re-parented
        // entries after their snapshot was taken, and a registration appears under its subject
        // and nowhere else (Requirement 3.5) - so a folder listing carries only what still has
        // this folder as its parent.
        return _entriesById.Values
            .Where(e => Equals(e.ParentId, folderId)
                        && _pathById.TryGetValue(e.Id, out var p)
                        && string.Equals(IoPath.GetDirectoryName(p), folderPath, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(e => e.IsFolder)
            .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Applies <see cref="HierarchyNesting"/>'s placement to one folder's entries: registrations
    /// re-parent under their subject files, subjects' <see cref="EntryNode.HasChildren"/> follow,
    /// and every change is pushed so a watching client converges without a reload.
    /// </summary>
    private void ApplyNesting(string folderPath, bool raiseEvents)
    {
        if (_catalog is null)
        {
            return;
        }

        var siblings = _entriesById.Values
            .Where(e => _pathById.TryGetValue(e.Id, out var p)
                        && string.Equals(IoPath.GetDirectoryName(p), folderPath, StringComparison.OrdinalIgnoreCase))
            .ToList();

        ShortGuid? folderParentId = null;
        if (!string.Equals(folderPath, _rootPath, StringComparison.OrdinalIgnoreCase) && _idByPath.TryGetValue(folderPath, out var ownId))
        {
            folderParentId = ownId;
        }

        var placements = HierarchyNesting.Assign(
            siblings.Select(sibling => (sibling.Name, sibling.IsFolder)).ToList(),
            name => ResolvedBodyNameOf(folderPath, name));

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

            var current = _entriesById[registration.Id];
            if (!Equals(current.ParentId, wantedParent))
            {
                _entriesById[registration.Id] = current with { ParentId = wantedParent };
                if (raiseEvents)
                {
                    EntryChanged?.Invoke(new HierarchyEntryUpdated(registration.Id, current.HasChildren, ParentChanged: true, ParentId: wantedParent, DiagramState: current.DiagramState));
                }
            }
        }

        // A subject file's HasChildren is exactly "does anything nest under it now".
        foreach (var file in siblings.Where(sibling => !sibling.IsFolder))
        {
            var current = _entriesById[file.Id];
            var hasChildren = _entriesById.Values.Any(e => e.ParentId == file.Id);
            if (current.HasChildren != hasChildren)
            {
                _entriesById[file.Id] = current with { HasChildren = hasChildren };
                if (raiseEvents)
                {
                    EntryChanged?.Invoke(new HierarchyEntryUpdated(file.Id, hasChildren, DiagramState: current.DiagramState));
                }
            }
        }
    }

    /// <summary>
    /// Computes every entry's <see cref="EntryDiagramState"/> for one scanned folder - its
    /// files against their sibling set, and the folder itself against its contents - and
    /// raises <see cref="HierarchyEntryUpdated"/> where a rescan changed one (small-refinements
    /// Requirements 3.1, 3.2, 3.6). The decision is <see cref="EntryDiagramStates.Decide"/>;
    /// everything here is the wiring, over the same name set the nesting pass just used, with
    /// each registration's body resolved once rather than per file.
    /// </summary>
    /// <summary>The same refresh, addressed by path - what the watcher handlers hold.</summary>
    private void RefreshDiagramStatesAt(string folderPath, bool raiseEvents)
    {
        if (string.Equals(folderPath, _rootPath, StringComparison.OrdinalIgnoreCase))
        {
            RefreshDiagramStates(null, folderPath, raiseEvents);
        }
        else if (_idByPath.TryGetValue(folderPath, out var folderId))
        {
            RefreshDiagramStates(folderId, folderPath, raiseEvents);
        }
    }

    private void RefreshDiagramStates(ShortGuid? folderId, string folderPath, bool raiseEvents)
    {
        if (_router is null || _editorResolver is null)
        {
            return; // A model built without routing (older tests, degraded hosts) stays neutral.
        }

        var contained = _entriesById.Values
            .Where(e => _pathById.TryGetValue(e.Id, out var p)
                        && string.Equals(IoPath.GetDirectoryName(p), folderPath, StringComparison.OrdinalIgnoreCase))
            .ToList();
        var names = contained.Select(entry => entry.Name).ToList();

        // Each registration's resolution and routing, once: rule 2 would otherwise re-read
        // every registration per sibling file (the NFR Performance clause).
        var bodyByRegistration = names
            .Where(DiagramFilePair.IsRegistrationFile)
            .ToDictionary(name => name, name => ResolvedBodyNameOf(folderPath, name), StringComparer.OrdinalIgnoreCase);
        string? BodyNameOf(string registration) => bodyByRegistration.GetValueOrDefault(registration);

        // An unreadable registration routes to DiagramUnreadable, which simply is not a
        // folder-subject type: neutral, never an exception out of the scan. The catalog probe
        // keeps deployments without folder-subject types from paying any reads at all.
        bool DeclaresFolderSubject(string registration) =>
            _router.HasFolderSubjectTypes
            && _router.Route(IoPath.Combine(folderPath, registration), _rootPath) is DiagramRouted { Definition.HasFolderSubject: true };

        void Apply(ShortGuid entryId, EntryDiagramState state, bool raise)
        {
            var current = _entriesById[entryId];
            if (current.DiagramState == state)
            {
                return;
            }

            _entriesById[entryId] = current with { DiagramState = state };
            if (raise)
            {
                EntryChanged?.Invoke(new HierarchyEntryUpdated(entryId, current.HasChildren, DiagramState: state));
            }
        }

        foreach (var file in contained.Where(entry => !entry.IsFolder))
        {
            Apply(file.Id, EntryDiagramStates.Decide(
                file.Name, isFolder: false, names, BodyNameOf, _router.ClaimsExtensionOf, _editorResolver.IsClaimed, DeclaresFolderSubject), raiseEvents);
        }

        // The folder's own state comes from its contents, so it is decided here - in ITS scan -
        // rather than in its parent's, which never enumerates it. The root has no entry. Its
        // update is raised even on a listing-triggered scan (raiseEvents false): the listing's
        // response carries the folder's CHILDREN, never the folder itself, so the watch stream
        // is the only channel this change can reach the client on - found live, where the
        // folder stayed neutral after expanding it.
        if (folderId is { } id && _entriesById.TryGetValue(id, out var folder))
        {
            Apply(id, EntryDiagramStates.Decide(
                folder.Name, isFolder: true, names, BodyNameOf, _router.ClaimsExtensionOf, _editorResolver.IsClaimed, DeclaresFolderSubject), raise: true);
        }
    }

    /// <summary>
    /// The body file name a registration resolves inside its own folder, or null: no catalog,
    /// no resolution, or a body living elsewhere. The resolution itself is
    /// <see cref="DiagramFilePair"/>'s; only same-folder bodies nest, since a header can point
    /// across folders and relocating an entry into another folder's subtree is not this spec.
    /// </summary>
    private string? ResolvedBodyNameOf(string folderPath, string fileName)
    {
        if (_catalog is null)
        {
            return null;
        }

        var body = DiagramFilePair.BodyOf(IoPath.Combine(folderPath, fileName), _catalog, _rootPath);
        if (body is not { } resolved || resolved.Path.Length == 0)
        {
            return null;
        }

        return string.Equals(IoPath.GetDirectoryName(resolved.Path), folderPath, StringComparison.OrdinalIgnoreCase)
            ? IoPath.GetFileName(resolved.Path)
            : null;
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

        // The link probe touches the disk, and the entry may be gone by the time it runs: a
        // multi-file delete (a git checkout, say) raises one watcher event per file, and each
        // event re-resolves every tracked selection - whose files may already have vanished
        // while their model entries have not. Probing a vanished path throws, and this runs
        // on the watcher's thread, where an unhandled exception kills the whole process
        // (found by the diagram-workspace-tabs manual pass). A path that cannot be probed is
        // simply not contained: a gone entry resolves to nothing, same as an unknown one.
        string? linkTarget;
        try
        {
            linkTarget = File.ResolveLinkTarget(fullPath, returnFinalTarget: true)?.FullName;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }

        return linkTarget is null || linkTarget.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
    }
}
