using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.GartnerHypeCycleGraph.Tests;

/// <summary>Routes each command to its handler, as the host's dispatcher does, so real history can run them.</summary>
internal sealed class GhgTestDispatcher(IGhgDocumentStore documents) : ICommandDispatcher
{
    public Task<CommandResult> DispatchAsync(ICommand command, CancellationToken cancellationToken = default) => command switch
    {
        AddGhgTrendCommand c => new AddGhgTrendCommandHandler(documents).ExecuteAsync(c, cancellationToken),
        AddGhgTriggerCommand c => new AddGhgTriggerCommandHandler(documents).ExecuteAsync(c, cancellationToken),
        AddGhgNoteCommand c => new AddGhgNoteCommandHandler(documents).ExecuteAsync(c, cancellationToken),
        SetGhgNoteSizeCommand c => new SetGhgNoteSizeCommandHandler(documents).ExecuteAsync(c, cancellationToken),
        RemoveGhgElementCommand c => new RemoveGhgElementCommandHandler(documents).ExecuteAsync(c, cancellationToken),
        SetGhgPlacementCommand c => new SetGhgPlacementCommandHandler(documents).ExecuteAsync(c, cancellationToken),
        SetGhgSpanCommand c => new SetGhgSpanCommandHandler(documents).ExecuteAsync(c, cancellationToken),
        SetGhgBoundaryCommand c => new SetGhgBoundaryCommandHandler(documents).ExecuteAsync(c, cancellationToken),
        ClearGhgBoundariesCommand c => new ClearGhgBoundariesCommandHandler(documents).ExecuteAsync(c, cancellationToken),
        ArrangeGhgCommand c => new ArrangeGhgCommandHandler(documents).ExecuteAsync(c, cancellationToken),
        SetGhgPhasesCommand c => new SetGhgPhasesCommandHandler(documents).ExecuteAsync(c, cancellationToken),
        RenameGhgElementCommand c => new RenameGhgElementCommandHandler(documents).ExecuteAsync(c, cancellationToken),
        SetGhgTagsCommand c => new SetGhgTagsCommandHandler(documents).ExecuteAsync(c, cancellationToken),
        SetGhgDescriptionCommand c => new SetGhgDescriptionCommandHandler(documents).ExecuteAsync(c, cancellationToken),
        AddGhgInfluenceCommand c => new AddGhgInfluenceCommandHandler(documents).ExecuteAsync(c, cancellationToken),
        SetGhgAttachmentCommand c => new SetGhgAttachmentCommandHandler(documents).ExecuteAsync(c, cancellationToken),
        RemoveGhgInfluenceCommand c => new RemoveGhgInfluenceCommandHandler(documents).ExecuteAsync(c, cancellationToken),
        RestoreDocumentCommand<IGhgDocumentStore> c => new RestoreDocumentCommandHandler<IGhgDocumentStore>(documents).ExecuteAsync(c, cancellationToken),
        _ => throw new InvalidOperationException($"No handler is registered for {command.GetType().Name}."),
    };
}
