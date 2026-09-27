using EtAlii.Adp.Documents;
using Serilog;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.Rdf;

/// <inheritdoc cref="IRdfDocumentStore" />
/// <remarks>
/// <para>
/// <b>A thin use of the shared lifecycle (backend-centralization task 6).</b> Opening, the retries
/// before a refused read is believed, keeping the last good document through a reload that cannot
/// read (R2.4), clearing it only on the watcher's delete (R2.5) and ignoring this store's own save
/// (R2.3), and creating the folder a first save needs, are all
/// <see cref="WritableDocumentLifecycle{TDocument}"/>'s. What stays here is what is this module's: how
/// a body's text becomes an entry, the refusal to write one that does not parse, and telling the
/// sessions.
/// </para>
/// <para>
/// <b>The sessions hear about a reload only when the lifecycle installed a document.</b> One that
/// kept the last good document changed nothing a session shows.
/// </para>
/// </remarks>
public sealed class RdfDocumentStore : IRdfDocumentStore
{
    private static readonly ILogger _logger = Log.ForContext<RdfDocumentStore>();

    // A first open that cannot read opens as empty with a warning naming the path, which is the
    // lifecycle's own default and what this store did before it (R2.2) - so no unavailable document
    // is declared here.
    private readonly WritableDocumentLifecycle<RdfDocumentEntry> _lifecycle = new(
        (path, text) => Parse(path, LineDocument.Parse(text)),
        entry => entry.Document.Text);

    /// <inheritdoc />
    public event EventHandler<RdfDocumentChangedEventArgs>? Changed;

    /// <inheritdoc />
    public RdfDocumentEntry GetOrLoad(string path) => _lifecycle.GetOrLoad(path);

    /// <inheritdoc />
    public DocumentSaveResult Save(string path, RdfDocumentEntry entry)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        ArgumentNullException.ThrowIfNull(entry);
        if (!entry.IsUsable)
        {
            // Writing a document whose model is empty because it never parsed would replace a
            // file somebody can still fix with one this module invented.
            _logger.Warning("Refusing to write {Path}: it does not parse ({Error})", path, entry.Error);
            return DocumentSaveResult.Failure($"{IoPath.GetFileName(path)} does not parse, so it was not written. {entry.Error}");
        }

        // The document's own lines are authoritative and unchanged by writing them out, but what
        // they mean has changed - so the model is rebuilt from the document before it is saved,
        // and the lifecycle caches exactly that entry, whether or not the write lands.
        var reparsed = Parse(path, entry.Document);
        var result = _lifecycle.Save(path, reparsed);
        if (result.Failed)
        {
            return result;
        }

        Changed?.Invoke(this, new RdfDocumentChangedEventArgs(path, reparsed));
        return DocumentSaveResult.Ok;
    }

    /// <inheritdoc />
    public void Forget(string path) => _lifecycle.Forget(path);

    /// <inheritdoc />
    public void Reload(string path)
    {
        if (_lifecycle.Reload(path))
        {
            Changed?.Invoke(this, new RdfDocumentChangedEventArgs(path, _lifecycle.GetOrLoad(path)));
        }
    }

    /// <inheritdoc />
    public void BodyDeleted(string path)
    {
        if (_lifecycle.BodyDeleted(path))
        {
            Changed?.Invoke(this, new RdfDocumentChangedEventArgs(path, _lifecycle.GetOrLoad(path)));
        }
    }

    /// <summary>
    /// What the document states, or the reason nothing could be read from it. A file that is not
    /// Turtle is an ordinary state for a file somebody is editing, so it is carried as an entry
    /// with an error rather than thrown out of the store (Requirement 1.5).
    /// </summary>
    private static RdfDocumentEntry Parse(string path, LineDocument document)
    {
        try
        {
            return new RdfDocumentEntry(document, RdfParser.Parse(document), "", 0);
        }
        catch (RdfParseException exception)
        {
            _logger.Debug(exception, "{Path} does not parse at line {Line}", path, exception.Line);
            return new RdfDocumentEntry(document, RdfModel.Empty, exception.Message, exception.Line);
        }
    }
}
