using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.CausalLoopDiagram;

/// <summary>
/// What every causal loop diagram command handler shares: the store, the reading and the sentence a
/// document that could not be read is refused with. The run itself - load, edit a copy, save, and the
/// shared restore command as the undo, byte for byte (causal-loop-disl-fbl Requirement 5.7) - is
/// <see cref="DocumentEditRun"/>'s.
/// </summary>
/// <remarks>
/// An edit that asks for what the body already says is still saved and undone as one, as it always
/// was, so the history counts the same steps it did.
/// </remarks>
public abstract class CausalLoopEditHandler<TCommand> : DocumentEditHandler<TCommand, ICausalLoopDocumentStore, CausalLoopBody, CausalLoopModel>
    where TCommand : IDocumentEditCommand
{
    private readonly ICausalLoopDocumentStore _documents;

    /// <summary>A handler editing the documents of <paramref name="documents"/>.</summary>
    protected CausalLoopEditHandler(ICausalLoopDocumentStore documents)
        : base(documents)
    {
        _documents = documents;
    }

    /// <inheritdoc />
    /// <remarks>A document that could not be read is refused in this module's own sentence, as it always was.</remarks>
    protected override async Task<CommandResult> RunAsync(TCommand command, CancellationToken cancellationToken) =>
        _documents.GetOrLoad(command.BodyPath).IsUsable ? await base.RunAsync(command, cancellationToken) : CommandResult.Failure(CausalLoopEdits.Unreadable);

    /// <inheritdoc />
    protected sealed override CausalLoopModel Parse(CausalLoopBody body) => body.Read();
}
