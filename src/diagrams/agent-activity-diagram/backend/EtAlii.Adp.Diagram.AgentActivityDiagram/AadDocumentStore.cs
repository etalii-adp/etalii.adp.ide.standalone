using EtAlii.Adp.Documents;

namespace EtAlii.Adp.Diagram.AgentActivityDiagram;

/// <summary>The open activity files, one per path, shared by every connection viewing one.</summary>
public interface IAadDocumentStore : IReloadableDocumentStore
{
    /// <summary>The file at <paramref name="path"/>, opened once and kept.</summary>
    AadDocumentEntry GetOrLoad(string path);

    /// <summary>
    /// Applies <paramref name="edit"/> and writes the result - to what the file holds at that
    /// moment, when another program changed it after <paramref name="basis"/> was read.
    /// </summary>
    /// <param name="basis">The entry the caller read.</param>
    /// <param name="edit">
    /// The edit, as a function of the body it is applied to. It is handed a body of its own to
    /// change, and may be handed one read afresh from the disk, so it finds what it edits by id.
    /// </param>
    /// <returns>The outcome, and the text that was written when it was.</returns>
    AadSaved Save(string path, AadDocumentEntry basis, Func<AadBody, AadEdit> edit);

    void Forget(string path);

    void BodyDeleted(string path);

    event EventHandler<AadDocumentChangedEventArgs>? Changed;
}

/// <summary>What a save did: its result, and the text before and after for the undo that reverses it.</summary>
public sealed record AadSaved(DocumentSaveResult Result, string Before, string After);

public sealed class AadDocumentChangedEventArgs(string path) : EventArgs
{
    public string Path { get; } = path;
}

/// <inheritdoc cref="IAadDocumentStore"/>
public sealed class AadDocumentStore : IAadDocumentStore
{
    private readonly WritableDocumentLifecycle<AadDocumentEntry> _lifecycle = new(
        (_, text) => AadDocumentEntry.Read(text),
        entry => entry.Document.Text,
        (_, unavailability, reason) => AadDocumentEntry.Unavailable(unavailability, reason));

    public event EventHandler<AadDocumentChangedEventArgs>? Changed;

    public AadDocumentEntry GetOrLoad(string path) => _lifecycle.GetOrLoad(path);

    public AadSaved Save(string path, AadDocumentEntry basis, Func<AadBody, AadEdit> edit)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(basis);
        ArgumentNullException.ThrowIfNull(edit);

        var before = basis.Document.Text;
        var after = before;

        // The lifecycle decides which entry the edit is applied to: the caller's, or one read
        // afresh when an agent wrote the file in between (Requirement 8.6). Either way the edit
        // gets a body of its own, so the cached one is never changed under a reader.
        var result = _lifecycle.Save(path, basis, current =>
        {
            if (!current.IsUsable)
            {
                return DocumentEdit<AadDocumentEntry>.Refused(
                    $"{Path.GetFileName(path)} could not be read, so it was not written: {current.Unreadable}");
            }

            before = current.Document.Text;
            var body = AadBody.Parse(before);
            var outcome = edit(body);
            if (!outcome.WasApplied)
            {
                return DocumentEdit<AadDocumentEntry>.Refused(outcome.Refusal!);
            }

            after = body.Text;
            return DocumentEdit<AadDocumentEntry>.Applied(new AadDocumentEntry(body, AadModel.Read(body)));
        });

        if (!result.Failed)
        {
            Changed?.Invoke(this, new AadDocumentChangedEventArgs(path));
        }

        return new AadSaved(result, before, after);
    }

    public void Forget(string path) => _lifecycle.Forget(path);

    public void Reload(string path)
    {
        if (_lifecycle.Reload(path))
        {
            Changed?.Invoke(this, new AadDocumentChangedEventArgs(path));
        }
    }

    public void BodyDeleted(string path)
    {
        if (_lifecycle.BodyDeleted(path))
        {
            Changed?.Invoke(this, new AadDocumentChangedEventArgs(path));
        }
    }
}
