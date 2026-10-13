using EtAlii.Adp.Documents;

namespace EtAlii.Adp.History;

/// <summary>
/// A <see cref="DocumentEditHandler{TCommand, TStore, TBody, TModel}"/> whose edit is saved against
/// the file as it is now, through <see cref="DocumentEditRun.RunOnCurrent"/>: for a body other
/// programs write while it is open.
/// </summary>
/// <typeparam name="TCommand">The command handled.</typeparam>
/// <typeparam name="TStore">The module's store, which saves against the current file and restores the undo.</typeparam>
/// <typeparam name="TBody">The body the store reads a document into.</typeparam>
/// <typeparam name="TModel">The model the module's parser reads from a body.</typeparam>
public abstract class CurrentDocumentEditHandler<TCommand, TStore, TBody, TModel>(TStore documents)
    : DocumentEditHandler<TCommand, TStore, TBody, TModel>(documents)
    where TCommand : IDocumentEditCommand
    where TStore : IEditSavingDocumentStore<TBody, TModel>
    where TBody : ITextBody<TBody>
{
    private readonly TStore _documents = documents;

    /// <inheritdoc />
    protected override CommandResult Execute(TCommand command) =>
        DocumentEditRun.RunOnCurrent<TStore, TBody, TModel>(_documents, command.BodyPath, command, Parse, (body, model) => Apply(body, model, command));
}
