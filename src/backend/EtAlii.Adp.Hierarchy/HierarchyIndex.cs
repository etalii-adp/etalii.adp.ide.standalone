using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Hierarchy;

/// <summary>
/// The entries of a <see cref="HierarchyModel"/>, found by id, by path, by parent and by the folder
/// they live in, so that a folder's children are a lookup and a subtree's removal touches only the
/// subtree. A nested registration's parent is its subject, but it lives in its subject's folder,
/// which is why the parent and the folder are two lookups. Not thread-safe: the model uses it
/// under its own lock.
/// </summary>
internal sealed class HierarchyIndex
{
    private readonly Dictionary<ShortGuid, EntryNode> _entries = new();
    private readonly Dictionary<ShortGuid, string> _paths = new();
    private readonly Dictionary<string, ShortGuid> _ids = new(StringComparer.OrdinalIgnoreCase);

    // A ShortGuid? cannot key a dictionary, so the root's children are a set of their own.
    private readonly HashSet<ShortGuid> _rootChildren = new();
    private readonly Dictionary<ShortGuid, HashSet<ShortGuid>> _childrenByParent = new();
    private readonly Dictionary<string, HashSet<ShortGuid>> _idsByFolder = new(StringComparer.OrdinalIgnoreCase);

    public EntryNode this[ShortGuid id] => _entries[id];

    /// <summary>Every folder entry.</summary>
    public IEnumerable<EntryNode> Folders => _entries.Values.Where(entry => entry.IsFolder);

    public bool Contains(ShortGuid id) => _entries.ContainsKey(id);

    public bool TryGet(ShortGuid id, out EntryNode entry) => _entries.TryGetValue(id, out entry!);

    public bool TryGetPath(ShortGuid id, out string path) => _paths.TryGetValue(id, out path!);

    public bool TryGetId(string path, out ShortGuid id) => _ids.TryGetValue(path, out id);

    public bool ContainsPath(string path) => _ids.ContainsKey(path);

    /// <summary>The entries whose parent is <paramref name="parentId"/>; the root's when null.</summary>
    public IEnumerable<EntryNode> ChildrenOf(ShortGuid? parentId) =>
        ChildIdsOf(parentId) is { } ids ? ids.Select(id => _entries[id]) : [];

    public bool HasChildren(ShortGuid parentId) => _childrenByParent.TryGetValue(parentId, out var ids) && ids.Count > 0;

    /// <summary>The entries whose file or folder lives directly in <paramref name="folderPath"/>, whatever their parent.</summary>
    public IEnumerable<EntryNode> In(string folderPath) =>
        _idsByFolder.TryGetValue(folderPath, out var ids) ? ids.Select(id => _entries[id]) : [];

    public void Add(EntryNode entry, string path)
    {
        _entries[entry.Id] = entry;
        Link(entry.Id, entry.ParentId);
        Place(entry.Id, path);
    }

    /// <summary>Replaces an entry by its id, moving it under its new parent when that changed.</summary>
    public void Set(EntryNode entry)
    {
        var current = _entries[entry.Id];
        if (!Equals(current.ParentId, entry.ParentId))
        {
            Unlink(entry.Id, current.ParentId);
            Link(entry.Id, entry.ParentId);
        }

        _entries[entry.Id] = entry;
    }

    /// <summary>Removes an entry and everything under it; returns the ids removed.</summary>
    public IReadOnlyList<ShortGuid> RemoveSubtree(ShortGuid id)
    {
        var removed = new List<ShortGuid>();
        Remove(id, removed);
        return removed;
    }

    /// <summary>
    /// Gives an entry its new path. Only a FOLDER carries anything with it, and it carries every
    /// entry that lives in it: a file's children are its nested registrations, which live beside
    /// it and do not move when it is renamed, while a registration nested under a subject in a
    /// folder moves with the folder although its parent is the subject.
    /// </summary>
    /// <remarks>
    /// Rewriting a file's nested registrations as if they sat under it corrupted them into paths
    /// under a file, and that fired on every save: AdpFileWriter publishes by moving a scratch file
    /// onto the destination, and the watcher reports the swap as renames of the subject. After it
    /// the registrations were invisible to every sibling computation, could never re-nest, and
    /// reached the client flattened to the folder.
    /// </remarks>
    public void Move(ShortGuid id, string newPath)
    {
        if (_entries[id].IsFolder && _paths.TryGetValue(id, out var folderPath))
        {
            foreach (var inside in In(folderPath).Select(entry => entry.Id).ToList())
            {
                Move(inside, IoPath.Combine(newPath, IoPath.GetFileName(_paths[inside])));
            }
        }

        Displace(id);
        Place(id, newPath);
    }

    private void Remove(ShortGuid id, List<ShortGuid> removed)
    {
        if (_childrenByParent.TryGetValue(id, out var children))
        {
            foreach (var child in children.ToList())
            {
                Remove(child, removed);
            }

            _childrenByParent.Remove(id);
        }

        if (_entries.Remove(id, out var entry))
        {
            Unlink(id, entry.ParentId);
        }

        Displace(id);
        removed.Add(id);
    }

    private HashSet<ShortGuid>? ChildIdsOf(ShortGuid? parentId) =>
        parentId is { } id
            ? _childrenByParent.GetValueOrDefault(id)
            : _rootChildren;

    private void Link(ShortGuid id, ShortGuid? parentId)
    {
        if (parentId is { } parent)
        {
            if (!_childrenByParent.TryGetValue(parent, out var children))
            {
                children = new HashSet<ShortGuid>();
                _childrenByParent[parent] = children;
            }

            children.Add(id);
        }
        else
        {
            _rootChildren.Add(id);
        }
    }

    private void Unlink(ShortGuid id, ShortGuid? parentId) => ChildIdsOf(parentId)?.Remove(id);

    private void Place(ShortGuid id, string path)
    {
        _paths[id] = path;
        _ids[path] = id;
        if (IoPath.GetDirectoryName(path) is { } folder)
        {
            if (!_idsByFolder.TryGetValue(folder, out var ids))
            {
                ids = new HashSet<ShortGuid>();
                _idsByFolder[folder] = ids;
            }

            ids.Add(id);
        }
    }

    private void Displace(ShortGuid id)
    {
        if (!_paths.Remove(id, out var path))
        {
            return;
        }

        _ids.Remove(path);

        if (IoPath.GetDirectoryName(path) is { } folder && _idsByFolder.TryGetValue(folder, out var ids))
        {
            ids.Remove(id);
        }
    }
}
