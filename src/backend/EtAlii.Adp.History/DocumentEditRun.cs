using EtAlii.Adp.Documents;

namespace EtAlii.Adp.History;

/// <summary>
/// The one shape of an edit of a document's body: load it, edit a copy, save it, and hand back its
/// undo (code-structure FR-033). Nine modules each wrote this as their own <c>Edits.Run</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Every undo is the shared restore command</b>: the document's whole text as it was, with the
/// command itself as the redo, so an undo gives back the original bytes however much moved. The one
/// exception is an edit whose <see cref="EditOutcome.Inverse"/> names its own undo.
/// </para>
/// <para>
/// <b>The edit is made on a copy</b> of the cached body, so a refusal halfway through leaves nothing
/// behind in memory for the next save to write. A document that could not be read is not edited at
/// all, and the result is built from the save by <see cref="CommandResult.FromSave"/>, so a warning
/// of the save reaches the user.
/// </para>
/// </remarks>
public static class DocumentEditRun
{
    /// <summary>
    /// Edits the document at <paramref name="bodyPath"/> in <paramref name="documents"/> with
    /// <paramref name="edit"/>, which is handed a copy of the body and the model
    /// <paramref name="parse"/> reads from it.
    /// </summary>
    /// <param name="self">The command being run: the redo of the undo this returns.</param>
    /// <param name="cancellationToken">Gives up a wait for another save of the document.</param>
    public static async Task<CommandResult> RunAsync<TStore, TBody, TModel>(
        TStore documents,
        string bodyPath,
        ICommand self,
        Func<TBody, TModel> parse,
        Func<TBody, TModel, EditOutcome> edit,
        CancellationToken cancellationToken)
        where TStore : IDiagramDocumentStore<TBody, TModel>
        where TBody : ITextBody<TBody>
    {
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentException.ThrowIfNullOrWhiteSpace(bodyPath);
        ArgumentNullException.ThrowIfNull(self);
        ArgumentNullException.ThrowIfNull(parse);
        ArgumentNullException.ThrowIfNull(edit);

        var entry = documents.GetOrLoad(bodyPath);
        if (!entry.IsUsable)
        {
            return CommandResult.Failure(
                $"{Path.GetFileName(bodyPath)} could not be read, so nothing can be edited until it can: {entry.Unreadable}");
        }

        var before = entry.Document.Text;
        var copy = TBody.Parse(before);
        var outcome = edit(copy, parse(copy));
        if (!outcome.WasApplied)
        {
            return CommandResult.Failure(outcome.Refusal!);
        }

        if (outcome.IsUnchanged)
        {
            return CommandResult.Success();
        }

        var saved = await documents.SaveAsync(bodyPath, copy, cancellationToken);
        return CommandResult.FromSave(saved, outcome.Inverse ?? new RestoreDocumentCommand<TStore>(bodyPath, before, self));
    }

    /// <summary>
    /// Edits the document at <paramref name="bodyPath"/> as <see cref="RunAsync"/> does, but against the
    /// file as it is when the edit is saved: where another program wrote it since it was read, the
    /// edit is applied to what that program wrote.
    /// </summary>
    /// <remarks>
    /// The undo restores the text the edit was applied to, and refuses once the file is no longer
    /// what the edit left, so it never writes over a later change of the other program's. An
    /// <see cref="EditOutcome.Inverse"/> is not used here: only the whole text can make that check.
    /// Nor is <see cref="EditOutcome.Unchanged"/>: the edit is applied inside the store's save.
    /// </remarks>
    /// <param name="self">The command being run: the redo of the undo this returns.</param>
    /// <param name="cancellationToken">Gives up a wait for another save of the document.</param>
    public static async Task<CommandResult> RunOnCurrentAsync<TStore, TBody, TModel>(
        TStore documents,
        string bodyPath,
        ICommand self,
        Func<TBody, TModel> parse,
        Func<TBody, TModel, EditOutcome> edit,
        CancellationToken cancellationToken)
        where TStore : IEditSavingDocumentStore<TBody, TModel>
    {
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentException.ThrowIfNullOrWhiteSpace(bodyPath);
        ArgumentNullException.ThrowIfNull(self);
        ArgumentNullException.ThrowIfNull(parse);
        ArgumentNullException.ThrowIfNull(edit);

        var entry = documents.GetOrLoad(bodyPath);
        if (!entry.IsUsable)
        {
            return CommandResult.Failure(
                $"{Path.GetFileName(bodyPath)} could not be read, so nothing can be edited until it can: {entry.Unreadable}");
        }

        var saved = await documents.SaveAsync(bodyPath, entry, body => edit(body, parse(body)).Refusal ?? "", cancellationToken);
        return CommandResult.FromSave(saved.Result, new RestoreDocumentCommand<TStore>(bodyPath, saved.Before, self, saved.After));
    }
}
