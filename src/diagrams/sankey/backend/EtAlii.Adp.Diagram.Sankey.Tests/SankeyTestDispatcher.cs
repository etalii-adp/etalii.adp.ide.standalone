using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.Sankey.Tests;

/// <summary>Routes each Sankey command to its handler, as the host's dispatcher does, so real history can run them.</summary>
internal sealed class SankeyTestDispatcher(ISankeyDocumentStore documents) : ICommandDispatcher
{
    public Task<CommandResult> DispatchAsync(ICommand command, CancellationToken cancellationToken = default) => command switch
    {
        AddSankeyNodeCommand add => new AddSankeyNodeCommandHandler(documents).ExecuteAsync(add, cancellationToken),
        ConnectSankeyNodesCommand connect => new ConnectSankeyNodesCommandHandler(documents).ExecuteAsync(connect, cancellationToken),
        RemoveSankeyEntryCommand remove => new RemoveSankeyEntryCommandHandler(documents).ExecuteAsync(remove, cancellationToken),
        MoveSankeyNodeCommand move => new MoveSankeyNodeCommandHandler(documents).ExecuteAsync(move, cancellationToken),
        SetSankeyPropertyCommand set => new SetSankeyPropertyCommandHandler(documents).ExecuteAsync(set, cancellationToken),
        StepSankeyValueCommand step => new StepSankeyValueCommandHandler(documents).ExecuteAsync(step, cancellationToken),
        ScaleSankeyThicknessCommand scale => new ScaleSankeyThicknessCommandHandler(documents).ExecuteAsync(scale, cancellationToken),
        RestoreDocumentCommand<ISankeyDocumentStore> restore => new RestoreDocumentCommandHandler<ISankeyDocumentStore>(documents).ExecuteAsync(restore, cancellationToken),
        _ => throw new InvalidOperationException($"No handler is registered for {command.GetType().Name}."),
    };
}
