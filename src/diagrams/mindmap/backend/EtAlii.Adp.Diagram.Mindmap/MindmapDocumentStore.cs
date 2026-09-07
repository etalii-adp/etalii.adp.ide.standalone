using System.Collections.Concurrent;
using EtAlii.Adp.Common;
using EtAlii.Adp.Documents;
using Serilog;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.Mindmap;

/// <summary>
/// One <see cref="MindmapDocument"/> per open map, keyed by body path. Owns loading, atomic
/// saving, and telling whoever is listening what changed. Per-connection state - which
/// branches a viewer has folded, which viewport it reported - is layered on top by the
/// session (Requirement 9.4), never kept here, so the document stays one thing for everyone.
/// </summary>
public sealed class MindmapDocumentStore : IMindmapDocumentStore
{
    private static readonly ILogger Logger = Log.ForContext<MindmapDocumentStore>();

    private readonly ConcurrentDictionary<string, MindmapDocument> _documents = new(StringComparer.OrdinalIgnoreCase);

    // The paths this store is writing right now, so its own save does not bounce back through
    // Reload as an "external" change - PlainEditorSession's saving guard, per path.
    private readonly ConcurrentDictionary<string, byte> _selfWrites = new(StringComparer.OrdinalIgnoreCase);

    private readonly IDiagramDocumentFactory _factory;

    public MindmapDocumentStore(IDiagramDocumentFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _factory = factory;
    }

    /// <summary>A store over the module's own factory; the shape the host and most tests want.</summary>
    public MindmapDocumentStore()
        : this(new MindmapDocumentFactory())
    {
    }

    public event EventHandler<MindmapChangedEventArgs>? Changed;

    public MindmapDocument GetOrLoad(string bodyPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bodyPath);
        return _documents.GetOrAdd(bodyPath, Load);
    }

    public MindmapDocument? Get(string bodyPath) =>
        _documents.TryGetValue(bodyPath, out var document) ? document : null;

    public void Save(string bodyPath, MindmapChange change)
    {
        ArgumentNullException.ThrowIfNull(change);
        if (!_documents.TryGetValue(bodyPath, out var document))
        {
            throw new InvalidOperationException($"No document is loaded for {bodyPath}.");
        }

        // Temp-then-move in the same folder, so a reader sees the old map or the new one and
        // never a partial file (Requirement 3.7). That algorithm is AdpFileWriter's, including
        // the scratch-name pattern the hierarchy watcher ignores and the UTF-8-without-BOM
        // encoding this store has always written; it was hand-rolled here only because the
        // writer could not overwrite, which it now can.
        //
        // The self-write guard stays here rather than moving: it is this store's own
        // arrangement with its file watcher, not part of publishing a file.
        _selfWrites[bodyPath] = 1;
        try
        {
            AdpFileWriter.Save(bodyPath, document.ToText());
        }
        finally
        {
            _selfWrites.TryRemove(bodyPath, out _);
        }

        Logger.Debug("Saved {BodyPath} after {Change}", bodyPath, change.GetType().Name);
        Changed?.Invoke(this, new MindmapChangedEventArgs(bodyPath, change));
    }

    /// <summary>Drops a loaded document, so the next ask re-reads the file - after an external edit, or when its last viewer left.</summary>
    public void Release(string bodyPath) => _documents.TryRemove(bodyPath, out _);

    /// <summary>Re-reads a map changed on disk outside ADP and announces it (Requirement 11.8).</summary>
    public void Reload(string bodyPath)
    {
        if (_selfWrites.ContainsKey(bodyPath))
        {
            // The change on disk is this store's own save, mid-write; Save announces it itself.
            return;
        }

        if (!_documents.ContainsKey(bodyPath))
        {
            return;
        }

        _documents[bodyPath] = Load(bodyPath);
        Changed?.Invoke(this, new MindmapChangedEventArgs(bodyPath, new MindmapReloaded()));
    }

    private MindmapDocument Load(string bodyPath)
    {
        var text = File.Exists(bodyPath) ? SharedDocumentReader.ReadAllText(bodyPath) : "";
        if (string.IsNullOrWhiteSpace(text))
        {
            // A registration without its body - or with an empty one, which a zero-byte file
            // dropped into the project amounts to - is a recoverable state: open empty, and
            // the body is written by the first save (Requirement 2.5). Parsing "" as XML
            // would otherwise throw here on every open attempt.
            Logger.Information("No readable body at {BodyPath}; opening an empty map", bodyPath);
            var baseName = IoPath.GetFileNameWithoutExtension(bodyPath);
            return MindmapDocument.Parse(_factory.CreateEmptyDocument(baseName));
        }

        try
        {
            return MindmapDocument.Parse(text);
        }
        catch (MindmapFormatException exception)
        {
            // Named, so the user knows which file is broken; rethrown, so only this diagram fails.
            Logger.Warning(exception, "Could not read {BodyPath} as a Freeplane map", bodyPath);
            throw new MindmapFormatException($"{IoPath.GetFileName(bodyPath)}: {exception.Message}", exception);
        }
    }
}
