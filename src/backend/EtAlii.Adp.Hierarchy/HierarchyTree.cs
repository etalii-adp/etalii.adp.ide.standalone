using Serilog;
using static EtAlii.Adp.Hierarchy.HierarchyFolderReader;
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Hierarchy;

/// <summary>
/// The changes to a <see cref="HierarchyModel"/>'s entries: what a folder's read and the watcher's
/// events say is applied to its <see cref="HierarchyIndex"/>, and every change is handed to the
/// model to raise. It reads no disk - <see cref="HierarchyFolderReader"/> has read it already - and
/// it is not thread-safe: the model uses it under its own lock, and raises what it queued after.
/// </summary>
internal sealed class HierarchyTree
{
    private static readonly ILogger _logger = Log.ForContext<HierarchyTree>();

    private readonly string _rootPath;
    private readonly HierarchyIndex _index;
    private readonly Action<HierarchyEntryChange> _queue;
    private readonly HashSet<ShortGuid> _listedFolderIds = new();
    private bool _rootListed;

    /// <param name="rootPath">The absolute project root the entries live under.</param>
    /// <param name="index">The model's entries.</param>
    /// <param name="queue">Queues a change for the model to raise once its lock is released.</param>
    public HierarchyTree(string rootPath, HierarchyIndex index, Action<HierarchyEntryChange> queue)
    {
        _rootPath = rootPath;
        _index = index;
        _queue = queue;
    }

    /// <summary>
    /// A folder whose nesting and states an event changed, with the names it held then: the
    /// registrations among them are read, and the states decided, outside the lock.
    /// </summary>
    public sealed record Refresh(string FolderPath, string? FolderName, IReadOnlyList<(string Name, bool IsFolder)> Names);

    public Refresh? OnCreated(string path, EventFacts facts)
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
        if (IsRoot(parentPath))
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
        _queue(new HierarchyEntryCreated(node));

        // A created registration nests immediately, and a created subject adopts its orphans -
        // both are re-parents pushed as updates, so a client applying them in order arrives at
        // the same tree a fresh listing would build (Requirements 10.1, 10.3, 10.4). An .adp
        // appearing changes its subject's state; the update rides the same stream
        // (small-refinements Requirement 3.6).
        return RefreshOf(parentPath);
    }

    public Refresh? OnRemoved(string path, EventFacts facts)
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
                _queue(new HierarchyEntryUpdated(child.Id, reparented.HasChildren, ParentChanged: true, ParentId: folderParent, DiagramState: reparented.DiagramState));
            }
        }

        RemoveSubtree(id);
        _queue(new HierarchyEntryRemoved(id));

        // The reverse of the create: a deleted .adp downgrades its subject's state
        // (small-refinements Requirement 3.6).
        return parentPath is null ? null : RefreshOf(parentPath);
    }

    public Refresh? OnRenamed(string oldPath, string newPath)
    {
        if (!_index.TryGetId(oldPath, out var id))
        {
            return null; // never known on this connection
        }

        var newName = IoPath.GetFileName(newPath);
        _index.Move(id, newPath);
        _index.Set(_index[id] with { Name = newName });
        _queue(new HierarchyEntryRenamed(id, newName));

        // A rename can change what nests where - a subject gaining or losing its name, a
        // registration changing which subject it derives.
        return IoPath.GetDirectoryName(newPath) is { } directory ? RefreshOf(directory) : null;
    }

    /// <summary>Applies a refreshed folder's nesting and states: the root's, or those of a folder this connection knows.</summary>
    public void ApplyRefresh(Refresh refresh, FolderView view)
    {
        ApplyNesting(refresh.FolderPath, view.Placements, raiseEvents: true);

        // The states of the root, or of a folder this connection knows; another's stay unread.
        if (IsRoot(refresh.FolderPath))
        {
            ApplyStates(null, refresh.FolderPath, view.States, raiseEvents: true);
        }
        else if (_index.TryGetId(refresh.FolderPath, out var folderId))
        {
            ApplyStates(folderId, refresh.FolderPath, view.States, raiseEvents: true);
        }
    }

    /// <summary>
    /// Applies one folder's scan: new entries are added, known ones refreshed, and those known
    /// when the scan began that it did not find are removed; then the nesting and the states read
    /// with it. An entry the watcher added while the scan ran is left alone.
    /// </summary>
    public IReadOnlyList<EntryNode> ApplyFolder(ShortGuid? folderId, string folderPath, FolderRead read, HashSet<ShortGuid> knownBefore, bool raiseEvents)
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
                    _queue(new HierarchyEntryCreated(node));
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
                _queue(new HierarchyEntryRemoved(staleId));
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

    /// <summary>The folder's nesting and states to read again, with the names it holds now.</summary>
    private Refresh RefreshOf(string folderPath)
    {
        var folderName = !IsRoot(folderPath) && _index.TryGetId(folderPath, out var folderId)
            ? _index[folderId].Name
            : null;
        return new Refresh(folderPath, folderName, _index.In(folderPath).Select(entry => (entry.Name, entry.IsFolder)).ToList());
    }

    /// <summary>
    /// Updates an already-known folder's <see cref="EntryNode.HasChildren"/> to what the disk
    /// said and pushes an <see cref="HierarchyEntryUpdated"/> if it changed. A no-op for the root
    /// (it has no <see cref="EntryNode"/> of its own - the explorer's root level is always
    /// expanded) or a folder this connection doesn't know as an entry at all yet.
    /// </summary>
    private void RecomputeHasChildren(string folderPath, bool hasChildren)
    {
        if (IsRoot(folderPath))
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
        _queue(new HierarchyEntryUpdated(folderId, hasChildren, DiagramState: entry.DiagramState));
    }

    /// <summary>
    /// Applies <see cref="HierarchyNesting"/>'s placement to one folder's entries: registrations
    /// re-parent under their subject files, subjects' <see cref="EntryNode.HasChildren"/> follow,
    /// and every change is pushed so a watching client converges without a reload.
    /// </summary>
    private void ApplyNesting(string folderPath, IReadOnlyList<NestedEntry> placements, bool raiseEvents)
    {
        var siblings = _index.In(folderPath).ToList();

        ShortGuid? folderParentId = null;
        if (!IsRoot(folderPath) && _index.TryGetId(folderPath, out var ownId))
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
                    _queue(new HierarchyEntryUpdated(registration.Id, current.HasChildren, ParentChanged: true, ParentId: wantedParent, DiagramState: current.DiagramState));
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
                    _queue(new HierarchyEntryUpdated(file.Id, hasChildren, DiagramState: current.DiagramState));
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
                _queue(new HierarchyEntryUpdated(entryId, current.HasChildren, DiagramState: decision.State));
            }
        }
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

    private bool IsRoot(string folderPath) => string.Equals(folderPath, _rootPath, StringComparison.OrdinalIgnoreCase);
}
