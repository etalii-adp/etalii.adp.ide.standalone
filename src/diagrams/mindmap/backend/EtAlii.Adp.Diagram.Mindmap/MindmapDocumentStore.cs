using System.Collections.Concurrent;
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

    // The paths this store is writing, and what it last wrote to each, so its own save does not
    // bounce back through Reload as an "external" change - PlainEditorSession's saving guard, per
    // path.
    private readonly SelfWriteGuard _selfWrites = new();

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

    public DocumentSaveResult Save(string bodyPath, MindmapDocument document, MindmapChange change)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(change);

        // THE DOCUMENT WRITTEN IS THE ONE THE CALLER EDITED, never one re-fetched from the cache.
        // This signature already took a second argument and so looked as though it did that - but
        // the argument is the change to ANNOUNCE, not the document to write, and the document was
        // looked up here. Reload assigns _documents[bodyPath] = Load(bodyPath), so a reload landing
        // between a command's edit and its save wrote the re-read map and returned Ok - the
        // command's inverse then went onto the undo stack for a change the file never received,
        // and mindmap's inverses carry state, so a subtree that was never removed gets restored.
        // Measured in six other stores and fixed at 6c4f90d6; this store, c4 and causal-loop were
        // missed there because their saves re-fetch by some route other than GetOrLoad.
        //
        // The "never loaded" refusal STAYS, and it is deliberately a membership test rather than a
        // fetch: it asks whether this path is loaded without making the answer the thing written.
        // Task 4 pinned this throw with a test and task 10 owns whether it should remain, so the
        // lost-edit fix leaves that decision exactly where it was rather than settling it in
        // passing.
        if (!_documents.ContainsKey(bodyPath))
        {
            // Still an exception, and deliberately: saving a map this store never loaded is a
            // programming error rather than an outcome a user can act on. R2.8's "skips documents
            // it never loaded" is about Reload, which returns early below.
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
        var text = document.ToText();
        _selfWrites.Begin(bodyPath, text);
        try
        {
            AdpFileWriter.Save(bodyPath, text);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // THE EDIT STAYS IN MEMORY, which is the whole of this change (R3.2, R3.4). Until now a
            // refused write threw out of here, out of the command handler and out to the caller as an
            // exception rather than a sentence - so a user whose file was held open by another program
            // was told nothing they could act on, and the command reported no failure a caller could
            // read. The document is left as the command edited it, so retrying once the file is
            // writable saves the same edit rather than asking the user to make it again.
            Logger.Warning(exception, "Could not write {BodyPath}; the change is kept in memory", bodyPath);
            return DocumentSaveResult.Failure($"{IoPath.GetFileName(bodyPath)} could not be written. The change is still here to try again.");
        }
        finally
        {
            _selfWrites.End(bodyPath);
        }

        // The document just written becomes the cached one, so the cache and the bytes on disk
        // cannot disagree. Ordinarily this is a no-op - the caller's document IS the cached object -
        // and it earns its place only in the raced case the argument above exists for: a reload
        // that replaced the entry mid-edit would otherwise leave the cache holding the re-read map
        // while the file holds the edit that was actually saved.
        _documents[bodyPath] = document;

        Logger.Debug("Saved {BodyPath} after {Change}", bodyPath, change.GetType().Name);
        Changed?.Invoke(this, new MindmapChangedEventArgs(bodyPath, change));
        return DocumentSaveResult.Ok;
    }

    /// <summary>Drops a loaded document, so the next ask re-reads the file - after an external edit, or when its last viewer left.</summary>
    public void Release(string bodyPath)
    {
        _documents.TryRemove(bodyPath, out _);
        _selfWrites.Forget(bodyPath);
    }

    /// <summary>Re-reads a map changed on disk outside ADP and announces it (Requirement 11.8).</summary>
    public void Reload(string bodyPath)
    {
        if (_selfWrites.IsOwnWrite(bodyPath))
        {
            // The change on disk is this store's own save, in flight or already landed; Save
            // announces it itself.
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
