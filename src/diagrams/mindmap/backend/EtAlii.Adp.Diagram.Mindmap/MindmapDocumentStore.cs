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
/// <remarks>
/// <para>
/// <b>A thin use of the shared lifecycle (backend-centralization task 6).</b> Opening, the retries
/// before a refused read is believed, keeping the last good document through a reload that cannot
/// read (R2.4), clearing it only on the watcher's delete (R2.5) and ignoring this store's own save
/// (R2.3), the atomic publish (Requirement 3.7) and creating the folder a first save needs, are all
/// <see cref="WritableDocumentLifecycle{TDocument}"/>'s. What stays here is what is this module's: how
/// a body's text becomes a map, and telling the sessions.
/// </para>
/// <para>
/// <b>Two departures from the other stores stay here, and they are permitted, not inconsistencies to
/// tidy away</b> (R2.8, task 10). Each is pinned by <c>MindmapDocumentStoreDepartureTests</c>:
/// </para>
/// <list type="number">
/// <item><description>
/// <b>A reload or a deletion of a map this store never loaded is skipped.</b> The shared lifecycle
/// would open it - it installs a document for any path it is asked about - so the check is kept here,
/// around the lifecycle call, rather than as an override of the lifecycle. A map nobody has open has
/// no session to tell, and opening one on a watcher event would cache a document nobody asked for.
/// </description></item>
/// <item><description>
/// <b>The change event names the kind of structural change</b>, rather than carrying the path and a
/// fresh model as the other stores' events do. A save announces the <see cref="MindmapChange"/> the
/// command made, and a reload or a deletion announces <see cref="MindmapReloaded"/>, so the session
/// can lay out only what moved (mindmap R5.3's structure-aware reaction).
/// </description></item>
/// </list>
/// <para>
/// <b>The sessions hear about a reload only when the lifecycle installed a document.</b> One that
/// kept the last good document changed nothing a session shows.
/// </para>
/// </remarks>
public sealed class MindmapDocumentStore : IMindmapDocumentStore
{
    private static readonly ILogger _logger = Log.ForContext<MindmapDocumentStore>();

    private readonly IDiagramDocumentFactory _factory;

    // A first open that cannot read opens as an empty map with a warning naming the path, which is
    // the lifecycle's own default (R2.2) and what the parse below makes of an empty text - so no
    // unavailable document is declared here.
    private readonly WritableDocumentLifecycle<MindmapDocument> _lifecycle;

    public MindmapDocumentStore(IDiagramDocumentFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _factory = factory;
        _lifecycle = new WritableDocumentLifecycle<MindmapDocument>(Parse, document => document.ToText());
    }

    /// <summary>A store over the module's own factory; the shape the host and most tests want.</summary>
    public MindmapDocumentStore()
        : this(new MindmapDocumentFactory())
    {
    }

    public event EventHandler<MindmapChangedEventArgs>? Changed;

    public MindmapDocument GetOrLoad(string bodyPath) => _lifecycle.GetOrLoad(bodyPath);

    public MindmapDocument? Get(string bodyPath) => _lifecycle.Get(bodyPath);

    public DocumentSaveResult Save(string bodyPath, MindmapDocument document, MindmapChange change)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(change);

        // The "never loaded" refusal STAYS, and it is deliberately a membership test rather than a
        // fetch: it asks whether this path is loaded without making the answer the thing written.
        // Saving a map this store never loaded is a programming error rather than an outcome a user
        // can act on, which is why it is still an exception. Task 4 pinned this throw with a test.
        if (_lifecycle.Get(bodyPath) is null)
        {
            throw new InvalidOperationException($"No document is loaded for {bodyPath}.");
        }

        // THE DOCUMENT WRITTEN IS THE ONE THE CALLER EDITED, never one re-fetched from the cache: a
        // reload landing between a command's edit and its save would otherwise have the save write
        // the re-read map and report success, and mindmap's inverses carry state, so a subtree that
        // was never removed gets restored. The lifecycle writes the caller's document and caches
        // exactly that object, whether or not the write lands - so a refused write leaves the edit in
        // memory to be retried (R3.2, R3.4).
        var result = _lifecycle.Save(bodyPath, document);
        if (result.Failed)
        {
            return result;
        }

        _logger.Debug("Saved {BodyPath} after {Change}", bodyPath, change.GetType().Name);

        // Departure 2 (R2.8): the change the command made, not a generic "the document changed".
        Changed?.Invoke(this, new MindmapChangedEventArgs(bodyPath, change));
        return result;
    }

    /// <summary>Re-reads a map changed on disk outside ADP and announces it (Requirement 11.8).</summary>
    public void Reload(string bodyPath)
    {
        if (!IsLoaded(bodyPath))
        {
            return;
        }

        if (_lifecycle.Reload(bodyPath))
        {
            Changed?.Invoke(this, new MindmapChangedEventArgs(bodyPath, new MindmapReloaded()));
        }
    }

    /// <summary>The watcher saw the body deleted: the map becomes what a first open of a missing body shows (R2.5).</summary>
    public void BodyDeleted(string bodyPath)
    {
        if (!IsLoaded(bodyPath))
        {
            return;
        }

        if (_lifecycle.BodyDeleted(bodyPath))
        {
            Changed?.Invoke(this, new MindmapChangedEventArgs(bodyPath, new MindmapReloaded()));
        }
    }

    /// <summary>
    /// Departure 1 (R2.8): whether a map is open here at all. A reload or a deletion of one that is
    /// not is skipped, because the lifecycle would open it.
    /// </summary>
    private bool IsLoaded(string bodyPath) => _lifecycle.Get(bodyPath) is not null;

    private MindmapDocument Parse(string bodyPath, string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            // A registration without its body - or with an empty one, which a zero-byte file
            // dropped into the project amounts to - is a recoverable state: open empty, and
            // the body is written by the first save (Requirement 2.5). Parsing "" as XML
            // would otherwise throw here on every open attempt.
            _logger.Information("No readable body at {BodyPath}; opening an empty map", bodyPath);
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
            // On a reload the lifecycle parses before it replaces, so the last good map stays.
            _logger.Warning(exception, "Could not read {BodyPath} as a Freeplane map", bodyPath);
            throw new MindmapFormatException($"{IoPath.GetFileName(bodyPath)}: {exception.Message}", exception);
        }
    }
}
