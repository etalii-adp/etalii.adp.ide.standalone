using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.AzureDevOpsPipeline;

/// <summary>
/// A handler of a command that edits a pipeline: the edit of a copy of the body, written through the
/// module's binding, saved, and undone by <see cref="RestorePipelineSpanCommand"/>. Each command says what it
/// does to a body.
/// </summary>
/// <remarks>
/// <para>
/// <b>Not the shared <see cref="DocumentEditHandler{TCommand, TStore, TBody, TModel}"/>, because a pipeline's
/// store answers only once it knows the workspace root, and the root comes with each command rather than once
/// with the handler.</b> The copy is opened by the store as it opens its own (<see cref="IPipelineDocumentStore.Open"/>),
/// so its templates are followed as the shared run's text-only copy would not follow them.
/// </para>
/// <para>
/// <b>Every edit is undone by the library's inverse of its splices</b>, carried by the host's command with the
/// digest of the bytes the edit left (amendment 2 of azure-devops-pipeline-disl-fbl): the undo puts back exactly
/// what the edit replaced, so the file is byte for byte what it was, and it is refused when the file has changed
/// since (FBL 7.2). The undo's own inverse is the redo, which writes the edit's bytes again.
/// </para>
/// </remarks>
/// <typeparam name="TCommand">The command handled.</typeparam>
internal abstract class PipelineEditHandler<TCommand>(IPipelineDocumentStore documents) : ICommandHandler<TCommand>
    where TCommand : IPipelineEditCommand
{
    /// <inheritdoc />
    public Task<CommandResult> ExecuteAsync(TCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        return RunAsync(documents, command, body => Apply(body, command), cancellationToken);
    }

    /// <summary>Changes <paramref name="body"/> as <paramref name="command"/> asks, through the definition and the binding, or says why not.</summary>
    protected abstract EditOutcome Apply(PipelineBody body, TCommand command);

    /// <summary>
    /// Edits a copy of the body <paramref name="command"/> names with <paramref name="edit"/> and saves it, with the
    /// undo that restores what the edit replaced; refused, with nothing written, where the edit is.
    /// </summary>
    private static async Task<CommandResult> RunAsync(IPipelineDocumentStore documents, IPipelineEditCommand command, Func<PipelineBody, EditOutcome> edit, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(edit);

        var entry = await documents.GetOrLoadAsync(command.Root, command.BodyPath, cancellationToken);
        if (!entry.IsUsable)
        {
            return CommandResult.Failure(
                $"{Path.GetFileName(command.BodyPath)} could not be read, so nothing can be edited until it can: {entry.Unreadable}");
        }

        // The edit is made on a copy, so a refusal halfway through leaves nothing behind for the next save to write.
        var before = entry.Document.Bytes;
        var copy = documents.Open(command.Root, command.BodyPath, before);
        var outcome = edit(copy);
        if (!outcome.WasApplied)
        {
            return CommandResult.Failure(outcome.Refusal!);
        }

        if (outcome.IsUnchanged || copy.LastEdit is not { } applied)
        {
            return CommandResult.Success();
        }

        var undo = RestorePipelineSpanCommand.Undoing(command, before, copy.Bytes, applied, isRedo: command is RestorePipelineSpanCommand { IsRedo: false });
        var saved = await documents.In(command.Root).SaveAsync(command.BodyPath, copy, cancellationToken);
        return CommandResult.FromSave(saved, undo);
    }
}
