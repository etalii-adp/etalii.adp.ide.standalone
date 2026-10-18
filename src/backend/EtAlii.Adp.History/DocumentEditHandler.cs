using EtAlii.Adp.Documents;

namespace EtAlii.Adp.History;

/// <summary>
/// A handler of a command that edits one document's body: the checks, the run through
/// <see cref="DocumentEditRun"/> and the result, written once (code-structure FR-033). A module
/// says how its model is read and what each command does to a body.
/// </summary>
/// <remarks>
/// A command that is refused before any document is read - a value that is not a number, a step of
/// nothing - overrides <see cref="RunAsync"/> and calls the base once it has checked.
/// </remarks>
/// <typeparam name="TCommand">The command handled.</typeparam>
/// <typeparam name="TStore">The module's store, which the undo restores through.</typeparam>
/// <typeparam name="TBody">The body the store reads a document into.</typeparam>
/// <typeparam name="TModel">The model the module's parser reads from a body.</typeparam>
public abstract class DocumentEditHandler<TCommand, TStore, TBody, TModel>(TStore documents) : ICommandHandler<TCommand>
    where TCommand : IDocumentEditCommand
    where TStore : IDiagramDocumentStore<TBody, TModel>
    where TBody : ITextBody<TBody>
{
    private readonly TStore _documents = documents ?? throw new ArgumentNullException(nameof(documents));

    /// <inheritdoc />
    public Task<CommandResult> ExecuteAsync(TCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        return RunAsync(command, cancellationToken);
    }

    /// <summary>Runs <paramref name="command"/> against its document.</summary>
    protected virtual Task<CommandResult> RunAsync(TCommand command, CancellationToken cancellationToken) =>
        DocumentEditRun.RunAsync<TStore, TBody, TModel>(_documents, command.BodyPath, command, Parse, (body, model) => Apply(body, model, command), cancellationToken);

    /// <summary>The model of <paramref name="body"/>, as the module's parser reads it.</summary>
    protected abstract TModel Parse(TBody body);

    /// <summary>Changes <paramref name="body"/>, whose model is <paramref name="model"/>, as <paramref name="command"/> asks, or says why not.</summary>
    protected abstract EditOutcome Apply(TBody body, TModel model, TCommand command);

}
