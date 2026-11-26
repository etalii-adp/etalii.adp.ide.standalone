using EtAlii.Adp.Documents;
using EtAlii.Adp.Files;
using EtAlii.Adp.Specification.Fbl.Links;
using Serilog;

namespace EtAlii.Adp.Links;

/// <summary>
/// The workspace's index of links (FBL 17.3), one <see cref="LinkWorkspace"/> per tracked root, filled by the registered
/// <see cref="ILinkIndexer"/>s and kept current from the root's one source of file events.
/// </summary>
/// <remarks>
/// <para>
/// <b>A file over <see cref="SizeLimit"/> is not read</b>, so that one large file cannot stall the index; it is held as
/// nothing until it shrinks. A document an indexer cannot read is held as nothing too, and nothing resolves to it.
/// </para>
/// <para>
/// <b>A document is held as its file is.</b> Every edit of a document is saved as it is made, so an open document never holds
/// what its file does not; what opening it finds and has not written is not the document's yet (etalii.adp spec 092, R6).
/// </para>
/// <para>
/// Paths are held with forward slashes and compared as text. Every change is made under one lock, and
/// <see cref="Changed"/> is raised after it, outside the lock.
/// </para>
/// </remarks>
public sealed class LinkIndex(ProjectFileEvents events, IEnumerable<ILinkIndexer> indexers, IFileWriter? writer = null) : ILinkIndex, IDisposable
{
    /// <summary>The size above which a file is not read, in bytes: 5 MB.</summary>
    public const long SizeLimit = 5L * 1024 * 1024;

    private static readonly ILogger _logger = Log.ForContext<LinkIndex>();

    private readonly ILinkIndexer[] _indexers = [.. indexers];
    private readonly Dictionary<string, Root> _roots = new(StringComparer.Ordinal);
    private readonly Lock _gate = new();

    /// <summary>A document of a tracked root changed in the index, by its path: added, read again or taken out.</summary>
    public event EventHandler<LinkIndexChangedEventArgs>? Changed;

    /// <inheritdoc />
    public IDisposable Track(string rootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        var key = Normalized(Path.GetFullPath(rootPath)).TrimEnd('/');
        lock (_gate)
        {
            if (!_roots.TryGetValue(key, out var root))
            {
                root = new Root(key);
                _roots[key] = root;
                ReadAll(root);
                root.Events = events.Subscribe(rootPath, heard => Heard(root, heard));
            }
            root.Holders++;
            return new Handle(this, root);
        }
    }

    /// <inheritdoc />
    public LinkWorkspace? WorkspaceOf(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var normalized = Normalized(path);
        lock (_gate)
        {
            return RootOf(normalized)?.Workspace;
        }
    }

    /// <inheritdoc />
    /// <inheritdoc />
    /// <remarks>
    /// Written through the one file writer, so the write is heard as a change: the index reads the new id from it, and a module
    /// that has the document open reads it again as it reads any change made outside it.
    /// </remarks>
    public async Task<string?> RenewAsync(IReadOnlyList<string> paths, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(paths);
        if (writer is null) return "Documents cannot be written here.";
        foreach (var path in paths)
        {
            var name = Path.GetFileName(path);
            if (IndexerOf(Normalized(path)) is not { } indexer) return $"{name} is no document that keeps an id.";
            byte[] bytes;
            try
            {
                bytes = await File.ReadAllBytesAsync(path, cancellationToken);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return $"{name} could not be read: {exception.Message}";
            }
            if (Renewed(indexer, path, bytes) is not { } renewed) return $"{name} could not be given a new document id.";
            await writer.SaveAsync(path, renewed, cancellationToken);
        }
        return null;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            foreach (var root in _roots.Values) root.Events?.Dispose();
            _roots.Clear();
        }
    }

    /// <summary>A path with forward slashes, as the index holds it.</summary>
    private static byte[]? Renewed(ILinkIndexer indexer, string path, byte[] bytes)
    {
        try
        {
            return indexer.Renewed(path, bytes);
        }
        catch (Exception exception)
        {
            _logger.Warning(exception, "{Path} could not be given a new document id", path);
            return null;
        }
    }

    private static string Normalized(string path) => path.Replace('\\', '/');

    /// <summary>The innermost tracked root that holds <paramref name="path"/>; null when none does.</summary>
    private Root? RootOf(string path) =>
        _roots.Values.Where(root => path.StartsWith(root.Path + "/", StringComparison.Ordinal)).MaxBy(root => root.Path.Length);

    private void Release(Root root)
    {
        lock (_gate)
        {
            if (--root.Holders > 0) return;
            root.Events?.Dispose();
            _roots.Remove(root.Path);
        }
    }

    /// <summary>Reads every document under the root into a new workspace.</summary>
    private void ReadAll(Root root)
    {
        var workspace = new LinkWorkspace(root.Path);
        try
        {
            var files = Directory.EnumerateFiles(root.Path, "*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint }).Select(Normalized).ToList();
            foreach (var file in files)
            {
                if (Load(file) is { } document) workspace.Put(document);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.Warning(exception, "The root {Root} could not be listed; its links are not indexed until it is tracked again", root.Path);
        }
        root.Workspace = workspace;
    }

    /// <summary>Reads the file at <paramref name="path"/> into the root's workspace; whether the index changed.</summary>
    private bool ReadFile(Root root, string path) => IndexerOf(path) is not null && Set(root, path, Load(path));

    /// <summary>The file at <paramref name="path"/> as its tool type indexes it; null when no indexer claims it, or it is too large, gone or unreadable.</summary>
    private IndexedDocument? Load(string path)
    {
        if (IndexerOf(path) is null) return null;
        try
        {
            var info = new FileInfo(path);
            return !info.Exists || info.Length > SizeLimit ? null : Index(path, SharedDocumentReader.ReadAllBytes(path));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.Debug(exception, "{Path} could not be read for the index; the next change reads it again", path);
            return null;
        }
    }

    /// <summary><paramref name="bytes"/> as the tool type of <paramref name="path"/> indexes them; null when none claims it or it cannot be read.</summary>
    private IndexedDocument? Index(string path, byte[] bytes)
    {
        if (IndexerOf(path) is not { } indexer || bytes.LongLength > SizeLimit) return null;
        try
        {
            return indexer.Index(path, bytes) is { } document ? document with { Path = path, Origin = indexer.Origin } : null;
        }
        catch (Exception exception)
        {
            _logger.Warning(exception, "{Path} could not be indexed; it holds nothing in the index until it changes", path);
            return null;
        }
    }

    /// <summary>
    /// Holds <paramref name="document"/> at <paramref name="path"/>, or nothing when it is null; whether the index changed. A workspace
    /// once handed out is never changed, so a resolution on another thread reads one state whole: a change makes a new one.
    /// </summary>
    private static bool Set(Root root, string path, IndexedDocument? document)
    {
        if (document is null && root.Workspace.Document(path) is null) return false;
        var workspace = new LinkWorkspace(root.Path, root.Workspace.Documents.Where(held => held.Path != path));
        if (document is not null) workspace.Put(document);
        root.Workspace = workspace;
        return true;
    }

    private ILinkIndexer? IndexerOf(string path) => _indexers.FirstOrDefault(indexer => indexer.Claims(path));

    /// <inheritdoc />
    public IReadOnlyList<LinkElementKind> CreatableKindsOf(string path) => IndexerOf(path)?.CreatableKinds ?? [];

    /// <inheritdoc />
    public string? NewElementIdOf(string path) => IndexerOf(path)?.NewElementId();

    /// <inheritdoc />
    public byte[]? WithElement(string path, byte[] bytes, string kind, string id, string name) => IndexerOf(path)?.WithElement(bytes, kind, id, name);

    private void Heard(Root root, FileEvent heard)
    {
        var changed = new List<string>();
        lock (_gate)
        {
            if (!_roots.ContainsKey(root.Path)) return;
            var path = Normalized(heard.Path);
            switch (heard.Kind)
            {
                case FileEventKind.Created or FileEventKind.Changed:
                    if (Directory.Exists(path)) changed.AddRange(ReadFolder(root, path));
                    else if (ReadFile(root, path)) changed.Add(path);
                    break;
                case FileEventKind.Deleted:
                    changed.AddRange(ForgetUnder(root, path));
                    break;
                case FileEventKind.Renamed:
                    changed.AddRange(ForgetUnder(root, Normalized(heard.OldPath!)));
                    if (Directory.Exists(path)) changed.AddRange(ReadFolder(root, path));
                    else if (ReadFile(root, path)) changed.Add(path);
                    break;
                case FileEventKind.Lost:
                    // What was lost cannot be named: the root is read again whole.
                    var before = root.Workspace.Documents.Select(document => document.Path).ToList();
                    ReadAll(root);
                    changed.AddRange(before.Union(root.Workspace.Documents.Select(document => document.Path)));
                    break;
            }
        }
        foreach (var path in changed) Raise(path);
    }

    /// <summary>Reads every document under the folder at <paramref name="folder"/>: one moved or copied in whole.</summary>
    private List<string> ReadFolder(Root root, string folder) =>
        [.. Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Select(Normalized).Where(file => ReadFile(root, file))];

    /// <summary>Takes out the document at <paramref name="path"/>, or every document under it when it was a folder.</summary>
    private static List<string> ForgetUnder(Root root, string path)
    {
        var gone = root.Workspace.Documents.Select(document => document.Path).Where(held => IsUnder(held, path)).ToList();
        if (gone.Count > 0) root.Workspace = new LinkWorkspace(root.Path, root.Workspace.Documents.Where(document => !IsUnder(document.Path, path)));
        return gone;
    }

    /// <summary>Whether <paramref name="held"/> is <paramref name="path"/> or a path under it.</summary>
    private static bool IsUnder(string held, string path) => held == path || held.StartsWith(path + "/", StringComparison.Ordinal);

    private void Raise(string path)
    {
        try
        {
            Changed?.Invoke(this, new LinkIndexChangedEventArgs(path));
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "A listener to the link index failed on {Path}; the index itself is current", path);
        }
    }

    /// <summary>One tracked root: its workspace, its subscription to the root's file events and how many hold it.</summary>
    private sealed class Root(string path)
    {
        public string Path { get; } = path;

        public LinkWorkspace Workspace { get; set; } = new(path);

        public IDisposable? Events { get; set; }

        public int Holders { get; set; }
    }

    /// <summary>One holder of a tracked root; disposing it lets go of the root once.</summary>
    private sealed class Handle(LinkIndex owner, Root root) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0) owner.Release(root);
        }
    }
}
