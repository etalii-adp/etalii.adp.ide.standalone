using EtAlii.Adp.Files;
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
/// reads from a body (<see cref="Parse"/>). Everything the shared lifecycle decides - the retries,
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

    /// <param name="writer">What publishes a save, waiting for another save of the body without blocking a thread.</param>
    protected LifecycleDocumentStore(IFileWriter writer)
    {
        _lifecycle = new WritableDocumentLifecycle<DocumentEntry<TBody, TModel>>(
            (_, text) => ReadEntry(text),
            entry => TextOf(entry.Document),
            writer,
            (_, unavailability, reason) => Unavailable(unavailability, reason));
    }

    /// <inheritdoc />
    public event EventHandler<DocumentChangedEventArgs>? Changed;

    /// <inheritdoc />
    public DocumentEntry<TBody, TModel> GetOrLoad(string path) => _lifecycle.GetOrLoad(path);

    /// <inheritdoc />
    public async Task<DocumentSaveResult> SaveAsync(string path, TBody document, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(document);

        var current = _lifecycle.Get(path);
        if (current is { IsUsable: false })
        {
            return DocumentSaveResult.Failure(
                $"{IoPath.GetFileName(path)} could not be read, so it was not written: {current.Unreadable}");
        }

        var entry = Entry(document);
        var result = await _lifecycle.SaveAsync(path, entry, cancellationToken);
        if (!result.Failed)
        {
            result = Kept(result, await SavedAsync(path, entry, cancellationToken));
        }

        OnChanged(path);
        return result;
    }

    /// <summary>
    /// Applies <paramref name="edit"/> and writes the result - to what the file holds at that
    /// moment, when another program changed it after <paramref name="basis"/> was read.
    /// </summary>
    /// <param name="path">The body path.</param>
    /// <param name="basis">The entry the caller read.</param>
    /// <param name="edit">
    /// The edit, as a function of the body it is applied to, returning an empty text when it was
    /// applied and the reason when it was refused. It is handed a body of its own to change, and
    /// may be handed one read afresh from the disk, so it finds what it edits by id.
    /// </param>
    /// <remarks>
    /// <para>
    /// <b>For a file other programs write while it is open</b>, as agents write an activity file:
    /// the plain <see cref="SaveAsync(string, TBody, CancellationToken)"/> would write the older text plus this one edit
    /// over their change. <see cref="WritableDocumentLifecycle{TDocument}"/> says what this narrows
    /// and what it cannot close.
    /// </para>
    /// <para>
    /// <b>The sessions hear about it only when it was written.</b> A refusal changed nothing.
    /// </para>
    /// </remarks>
    public async Task<DocumentEditSaved> SaveAsync(string path, DocumentEntry<TBody, TModel> basis, Func<TBody, string> edit, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(basis);
        ArgumentNullException.ThrowIfNull(edit);

        var before = TextOf(basis.Document);
        var after = before;

        // The lifecycle decides which entry the edit is applied to: the caller's, or one read afresh
        // when the file was written in between. Either way the edit gets a body of its own, so the
        // cached one is never changed under a reader.
        var result = await _lifecycle.SaveAsync(path, basis, current =>
        {
            if (!current.IsUsable)
            {
                return DocumentEdit<DocumentEntry<TBody, TModel>>.Refused(
                    $"{IoPath.GetFileName(path)} could not be read, so it was not written: {current.Unreadable}");
            }

            before = TextOf(current.Document);
            var body = Read(before);
            var refusal = edit(body);
            if (refusal.Length > 0)
            {
                return DocumentEdit<DocumentEntry<TBody, TModel>>.Refused(refusal);
            }

            after = TextOf(body);
            return DocumentEdit<DocumentEntry<TBody, TModel>>.Applied(Entry(body));
        }, cancellationToken);

        if (!result.Failed)
        {
            result = Kept(result, await SavedAsync(path, _lifecycle.GetOrLoad(path), cancellationToken));
            OnChanged(path);
        }

        return new DocumentEditSaved(result, before, after);
    }

    /// <inheritdoc />
    public void Reload(string path)
    {
        if (_lifecycle.Reload(path))
        {
            Installed(path);
            OnChanged(path);
        }
    }

    /// <inheritdoc />
    public void BodyDeleted(string path)
    {
        if (_lifecycle.BodyDeleted(path))
        {
            Installed(path);
            OnChanged(path);
        }
    }

    /// <inheritdoc />
    /// <remarks>A module that keeps something beside a document forgets it here too, and calls this.</remarks>
    public virtual void Forget(string path) => _lifecycle.Forget(path);

    /// <summary>The body a text reads as; an empty text is a new, empty document.</summary>
    protected abstract TBody Read(string text);

    /// <summary>The text <paramref name="body"/> is saved as.</summary>
    protected abstract string TextOf(TBody body);

    /// <summary>
    /// What the module's parser reads from <paramref name="body"/>, with what it passed over and whether
    /// the body could be read at all; a body that could not be is not written to until it can be.
    /// </summary>
    /// <remarks>
    /// The one hook a module answers with its reading (code-structure FR-049). A module whose reading
    /// cannot fail answers its model with no problems.
    /// </remarks>
    protected abstract ParseOutcome<TModel> Parse(TBody body);

    /// <summary>
    /// Empty for a body that was read; otherwise why its text, though it could be opened, cannot be
    /// read as a document of this type - and then nothing is written to it until it can be.
    /// </summary>
    /// <remarks>Empty unless the module says otherwise: most parsers read any text, reporting what they pass over.</remarks>
    protected virtual string UnreadableOf(TBody body) => "";

    /// <summary>
    /// What a module keeps beside a document, brought up to date with <paramref name="entry"/>, the
    /// entry a save of <paramref name="path"/> just wrote, before the sessions hear of it. Empty when
    /// all of it was kept, otherwise a warning the save carries to the user: the document landed and
    /// something beside it did not.
    /// </summary>
    /// <remarks>Nothing is kept beside a document unless the module says otherwise.</remarks>
    protected virtual Task<string> SavedAsync(string path, DocumentEntry<TBody, TModel> entry, CancellationToken cancellationToken) => Task.FromResult("");

    /// <summary>
    /// What a module keeps beside a document, brought up to date with the entry a reload or a
    /// deletion of <paramref name="path"/> just installed, before the sessions hear of it.
    /// </summary>
    protected virtual void Installed(string path)
    {
    }

    /// <summary><paramref name="result"/>, carrying <paramref name="warning"/> as well as any warning of its own.</summary>
    private static DocumentSaveResult Kept(DocumentSaveResult result, string warning) =>
        warning.Length == 0
            ? result
            : DocumentSaveResult.WithWarning(result.Warning.Length == 0 ? warning : $"{result.Warning} {warning}");

    private DocumentEntry<TBody, TModel> Entry(TBody body) => new(body, Parse(body));

    private DocumentEntry<TBody, TModel> ReadEntry(string text)
    {
        var body = Read(text);
        var outcome = Parse(body);
        return new(body, outcome.IsUsable ? outcome with { Unreadable = UnreadableOf(body) } : outcome);
    }

    // A missing body opens as a new, empty document, which is usable whatever its parser makes of no text.
    private DocumentEntry<TBody, TModel> Unavailable(DocumentUnavailability unavailability, string reason)
    {
        var empty = Entry(Read(""));
        return unavailability == DocumentUnavailability.Missing
            ? empty with { Unreadable = "" }
            : empty with { Unreadable = reason.Length > 0 ? reason : "the body could not be read" };
    }

    /// <summary>
    /// Tells the sessions on <paramref name="path"/> that its document changed: after a save, a
    /// reload or a deletion here, and where a module changes how a document is drawn without
    /// changing what it says.
    /// </summary>
    protected void OnChanged(string path) => Changed?.Invoke(this, new DocumentChangedEventArgs(path));
}
