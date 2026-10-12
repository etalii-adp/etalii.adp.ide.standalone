using IoPath = System.IO.Path;

namespace EtAlii.Adp.Documents;

/// <summary>
/// A diagram module's document store, written once: load and cache, reload, ignore the store's own
/// save, keep the last good document through a read that fails, end a deleted one, and save what
/// the caller edited (code-structure FR-029).
/// </summary>
/// <remarks>
/// <para>
/// <b>A module supplies three things and decides nothing else.</b> How a body's text becomes a body
/// (<see cref="Read"/>), how a body becomes text again (<see cref="TextOf"/>), and what its parser
/// reads from a body (<see cref="Rebuild"/>). Everything the shared lifecycle decides - the retries,
/// the self-write suppression, keep-last-good, the first open of a missing body - this store does
/// not decide again, and neither does the module.
/// </para>
/// <para>
/// <b>The sessions hear about a save whether or not it reached the disk</b>, because the lifecycle
/// keeps a failed write's edit in memory to be retried, and the canvas must show what it holds.
/// </para>
/// </remarks>
/// <typeparam name="TBody">The type a body is read into and edited as, such as <see cref="LineDocument"/>.</typeparam>
/// <typeparam name="TModel">What the module's parser reads from a body.</typeparam>
public abstract class LifecycleDocumentStore<TBody, TModel> : IDiagramDocumentStore<TBody, TModel>
{
    private readonly WritableDocumentLifecycle<DocumentEntry<TBody, TModel>> _lifecycle;

    protected LifecycleDocumentStore()
    {
        _lifecycle = new(
            (_, text) => Entry(Read(text)),
            entry => TextOf(entry.Document),
            (_, unavailability, reason) => Unavailable(unavailability, reason));
    }

    /// <inheritdoc />
    public event EventHandler<DocumentChangedEventArgs>? Changed;

    /// <inheritdoc />
    public DocumentEntry<TBody, TModel> GetOrLoad(string path) => _lifecycle.GetOrLoad(path);

    /// <inheritdoc />
    public DocumentSaveResult Save(string path, TBody document)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(document);

        var current = _lifecycle.Get(path);
        if (current is { IsUsable: false })
        {
            return DocumentSaveResult.Failure(
                $"{IoPath.GetFileName(path)} could not be read, so it was not written: {current.Unreadable}");
        }

        var result = _lifecycle.Save(path, Entry(document));
        OnChanged(path);
        return result;
    }

    /// <inheritdoc />
    public void Reload(string path)
    {
        if (_lifecycle.Reload(path))
        {
            OnChanged(path);
        }
    }

    /// <inheritdoc />
    public void BodyDeleted(string path)
    {
        if (_lifecycle.BodyDeleted(path))
        {
            OnChanged(path);
        }
    }

    /// <inheritdoc />
    public void Forget(string path) => _lifecycle.Forget(path);

    /// <summary>The body a text reads as; an empty text is a new, empty document.</summary>
    protected abstract TBody Read(string text);

    /// <summary>The text <paramref name="body"/> is saved as.</summary>
    protected abstract string TextOf(TBody body);

    /// <summary>What the module's parser reads from <paramref name="body"/>.</summary>
    protected abstract TModel Rebuild(TBody body);

    private DocumentEntry<TBody, TModel> Entry(TBody body) => new(body, Rebuild(body));

    private DocumentEntry<TBody, TModel> Unavailable(DocumentUnavailability unavailability, string reason)
    {
        var empty = Entry(Read(""));
        return unavailability == DocumentUnavailability.Missing
            ? empty
            : empty with { Unreadable = reason.Length > 0 ? reason : "the body could not be read" };
    }

    private void OnChanged(string path) => Changed?.Invoke(this, new DocumentChangedEventArgs(path));
}
