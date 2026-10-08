using EtAlii.Adp.Documents;

namespace EtAlii.Adp.Diagram.SupplyChain;

/// <summary>
/// The <c>.supply</c> document store: a thin use of the shared writable lifecycle.
/// </summary>
/// <remarks>
/// <para>
/// <b>Everything the lifecycle decides, this store does not decide again</b> - keep-last-good on a
/// reload that cannot read, the retries, the self-write suppression and the first open of a missing
/// body. What stays here is how a body's text becomes an entry, and telling the sessions.
/// </para>
/// <para>
/// <b>The sessions hear about a save whether or not it reached the disk</b>, because the lifecycle
/// keeps a failed write's edit in memory to be retried, and the canvas must show what it holds.
/// </para>
/// </remarks>
public sealed class SupplyChainDocumentStore : ISupplyChainDocumentStore
{
    private readonly WritableDocumentLifecycle<SupplyChainDocumentEntry> _lifecycle = new(
        (_, text) => SupplyChainDocumentEntry.Read(text),
        entry => entry.Document.Text,
        (_, unavailability, reason) => SupplyChainDocumentEntry.Unavailable(unavailability, reason));

    /// <inheritdoc />
    public event EventHandler<SupplyChainDocumentChangedEventArgs>? Changed;

    /// <inheritdoc />
    public SupplyChainDocumentEntry GetOrLoad(string path) => _lifecycle.GetOrLoad(path);

    /// <inheritdoc />
    public DocumentSaveResult Save(string path, LineDocument document)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(document);

        var current = _lifecycle.Get(path);
        if (current is { IsUsable: false })
        {
            return DocumentSaveResult.Failure(
                $"{Path.GetFileName(path)} could not be read, so it was not written: {current.Unreadable}");
        }

        var result = _lifecycle.Save(path, new SupplyChainDocumentEntry(document, SupplyChainParser.Parse(document)));
        Changed?.Invoke(this, new SupplyChainDocumentChangedEventArgs(path));
        return result;
    }

    /// <inheritdoc />
    public void Reload(string path)
    {
        if (_lifecycle.Reload(path))
        {
            Changed?.Invoke(this, new SupplyChainDocumentChangedEventArgs(path));
        }
    }

    /// <inheritdoc />
    public void BodyDeleted(string path)
    {
        if (_lifecycle.BodyDeleted(path))
        {
            Changed?.Invoke(this, new SupplyChainDocumentChangedEventArgs(path));
        }
    }
}
