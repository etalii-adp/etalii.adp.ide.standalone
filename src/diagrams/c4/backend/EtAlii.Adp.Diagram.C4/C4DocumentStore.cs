using EtAlii.Adp.Documents;
using Serilog;

namespace EtAlii.Adp.Diagram.C4;

/// <inheritdoc cref="IC4DocumentStore" />
/// <remarks>
/// <para>
/// <b>A thin use of the shared lifecycle (backend-centralization task 6).</b> Opening, the retries
/// before a refused read is believed, keeping the last good model through a reload that cannot read
/// (R2.4), clearing it only on the watcher's delete (R2.5), ignoring this store's own save (R2.3)
/// and creating a first save's folder are all <see cref="WritableDocumentLifecycle{TDocument}"/>'s.
/// This store carried its own copy of each from <c>350b8f9e</c> until the conversion, written to be
/// replaced by it; the conversion changed where the code lives, and its existing tests are the proof
/// that it did not change how it behaves.
/// </para>
/// <para>
/// <b>What stays here is what is this module's</b>: how a body's text becomes a document and a
/// workspace, the refusal to write an entry standing in for a body that could not be read, and
/// telling the sessions. The refusal is carried on the entry rather than in a set of paths, so it is
/// installed and cleared exactly when the lifecycle installs or replaces that entry.
/// </para>
/// </remarks>
public sealed class C4DocumentStore : IC4DocumentStore
{
    private static readonly ILogger _logger = Log.ForContext<C4DocumentStore>();

    // DocumentLifecycle's own count, named here only because this store's tests script exactly that
    // many refusals. The public constructor does not pass it: production uses the lifecycle's numbers.
    internal const int DefaultReadAttempts = 5;

    private readonly WritableDocumentLifecycle<C4DocumentEntry> _lifecycle;

    public C4DocumentStore()
    {
        _lifecycle = new WritableDocumentLifecycle<C4DocumentEntry>(Parse, Serialize, Unavailable);
    }

    /// <summary>
    /// With the read and its retry supplied, so a test can refuse exactly as often as it chooses and
    /// wait for nothing - deterministic rather than patient.
    /// </summary>
    internal C4DocumentStore(Func<string, string> read, int readAttempts, TimeSpan betweenReadAttempts)
    {
        ArgumentNullException.ThrowIfNull(read);
        ArgumentOutOfRangeException.ThrowIfLessThan(readAttempts, 1);
        _lifecycle = new WritableDocumentLifecycle<C4DocumentEntry>(Parse, Serialize, Unavailable, read, readAttempts, betweenReadAttempts);
    }

    public event EventHandler<C4DocumentChangedEventArgs>? Changed;

    public C4Document GetOrLoad(string path) => _lifecycle.GetOrLoad(path).Document;

    public C4Workspace WorkspaceOf(string path) => _lifecycle.GetOrLoad(path).Workspace;

    public DocumentSaveResult Save(string path, C4Document document)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(document);

        // THE DOCUMENT WRITTEN IS THE ONE THE CALLER EDITED, never one re-fetched from the cache.
        // Taking it as an argument is the enforcement: there is no cache read left here to get
        // wrong. Until this signature changed, Save re-fetched the entry itself while Reload
        // replaced it, so a reload landing between a command's ReplaceLine and its save wrote the
        // re-read file and returned "" for success - the command's inverse then went onto the undo
        // stack for a change the file never received. The lookup below reads only whether the
        // cached entry is a stand-in, never what to write.
        if (_lifecycle.Get(path) is { Unreadable: true })
        {
            // AN ENTRY STANDING IN FOR A FILE THAT COULD NOT BE READ IS NOT A DOCUMENT TO WRITE:
            // the file on disk is the only copy of the model left. Measured, one reload that could
            // not read the body followed by a save left a real .dsl file empty on disk.
            _logger.Warning("Refusing to write {Path}: it could not be read", path);
            return DocumentSaveResult.Failure($"{Path.GetFileName(path)} could not be read, so it was not written.");
        }

        // Parsed before the write, so the lifecycle caches exactly the entry that was written -
        // whether or not the write lands, which keeps a failed edit in memory to retry (R3.4).
        var entry = new C4DocumentEntry(document, C4Parser.Parse(document));
        var result = _lifecycle.Save(path, entry);
        if (result.Failed)
        {
            return result;
        }

        Changed?.Invoke(this, new C4DocumentChangedEventArgs(path, entry.Workspace));
        return DocumentSaveResult.Ok;
    }

    public void Touch(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        Changed?.Invoke(this, new C4DocumentChangedEventArgs(path, _lifecycle.GetOrLoad(path).Workspace));
    }

    /// <summary>Forgets a document, so the next open reads it afresh.</summary>
    public void Forget(string path) => _lifecycle.Forget(path);

    /// <summary>
    /// Re-reads a document an external tool changed, and tells the sessions on it - unless the
    /// reload kept the last good model, which changed nothing a session shows.
    /// </summary>
    public void Reload(string path)
    {
        if (_lifecycle.Reload(path))
        {
            Changed?.Invoke(this, new C4DocumentChangedEventArgs(path, _lifecycle.GetOrLoad(path).Workspace));
        }
    }

    /// <summary>The body was deleted: the model ends empty, and the sessions on it are told.</summary>
    public void BodyDeleted(string path)
    {
        if (_lifecycle.BodyDeleted(path))
        {
            Changed?.Invoke(this, new C4DocumentChangedEventArgs(path, _lifecycle.GetOrLoad(path).Workspace));
        }
    }

    private static C4DocumentEntry Parse(string path, string text)
    {
        var document = C4Document.Parse(text);
        return new C4DocumentEntry(document, C4Parser.Parse(document));
    }

    private static string Serialize(C4DocumentEntry entry) => entry.Document.ToText();

    // A missing body opens as a new, empty document; a body that is there and refused opens empty
    // too, but marked, so that emptiness is never written back over the file.
    private static C4DocumentEntry Unavailable(string path, DocumentUnavailability unavailability, string reason) =>
        Parse(path, "") with { Unreadable = unavailability == DocumentUnavailability.Unreadable };
}
