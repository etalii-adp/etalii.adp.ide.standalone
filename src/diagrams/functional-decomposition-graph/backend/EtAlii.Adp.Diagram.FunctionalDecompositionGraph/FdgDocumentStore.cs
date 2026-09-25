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
/// <b>Writable, although nothing here writes yet.</b> Task 12's commands save through this
/// lifecycle's <c>Save</c>. Holding the writable lifecycle from the start means a reload is already
/// checked against the store's own writes, so a save arriving later cannot bounce back as an
/// external change.
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
