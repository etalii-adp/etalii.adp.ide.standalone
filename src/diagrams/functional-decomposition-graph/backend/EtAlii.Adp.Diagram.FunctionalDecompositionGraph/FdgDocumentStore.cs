using EtAlii.Adp.Documents;

namespace EtAlii.Adp.Diagram.FunctionalDecompositionGraph;

/// <summary>
/// The <c>.fdg</c> document store: a thin use of <c>backend-centralization</c>'s shared lifecycle
/// (R2), written as the first module to take it whole rather than as a copy of one.
/// </summary>
/// <remarks>
/// <para>
/// <b>Everything the lifecycle decides, this store does not decide again.</b> Keep-last-good on a
/// reload that cannot read, the retries before a refused read is believed, the self-write
/// suppression, and the first open of a missing body are all
/// <see cref="WritableDocumentLifecycle{TDocument}"/>'s. What stays here is only what is FDG's:
/// how a body's text becomes an entry, and telling the sessions when one was replaced.
/// </para>
/// <para>
/// <b>The sessions hear about a change only when the lifecycle actually installed a document.</b>
/// A reload that kept the last good document changed nothing a session shows, so raising
/// <see cref="Changed"/> for it would only make every session re-render and diff to no effect.
/// </para>
/// <para>
/// <b>A save writes the document the command edited, never one looked up here</b>, which is the
/// shared lifecycle's own rule, and it refuses outright a document that could not be read - see
/// <see cref="FdgDocumentEntry.Unreadable"/>.
/// </para>
/// <para>
/// <b>The sessions hear about a save whether or not it reached the disk.</b> The lifecycle caches the
/// edited document either way: on a failed write it keeps the edit in memory to be retried, and the
/// next successful save writes it too. So after a failed write the cache already holds the edit,
/// and the canvas showing it is what the failure's own sentence - "the change is still here to try
/// again" - promises. Hiding it would leave the canvas and the cache disagreeing until the next
/// edit revealed both changes at once.
/// </para>
/// </remarks>
public sealed class FdgDocumentStore : IFdgDocumentStore
{
    private readonly WritableDocumentLifecycle<FdgDocumentEntry> _lifecycle = new(
        (_, text) => FdgDocumentEntry.Read(text),
        entry => entry.Document.Text,
        (_, unavailability, reason) => FdgDocumentEntry.Unavailable(unavailability, reason));

    /// <inheritdoc />
    public event EventHandler<FdgDocumentChangedEventArgs>? Changed;

    /// <inheritdoc />
    public FdgDocumentEntry GetOrLoad(string path) => _lifecycle.GetOrLoad(path);

    /// <inheritdoc />
    public DocumentSaveResult Save(string path, LineDocument document)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(document);

        var current = _lifecycle.Get(path);
        if (current is { IsUsable: false })
        {
            return DocumentSaveResult.Failure(
                $"{System.IO.Path.GetFileName(path)} could not be read, so it was not written: {current.Unreadable}");
        }

        var result = _lifecycle.Save(path, new FdgDocumentEntry(document, FdgParser.Parse(document)));
        Changed?.Invoke(this, new FdgDocumentChangedEventArgs(path));
        return result;
    }

    /// <inheritdoc />
    public void Forget(string path) => _lifecycle.Forget(path);

    /// <inheritdoc />
    public void Reload(string path)
    {
        if (_lifecycle.Reload(path))
        {
            Changed?.Invoke(this, new FdgDocumentChangedEventArgs(path));
        }
    }

    /// <inheritdoc />
    public void BodyDeleted(string path)
    {
        if (_lifecycle.BodyDeleted(path))
        {
            Changed?.Invoke(this, new FdgDocumentChangedEventArgs(path));
        }
    }
}
