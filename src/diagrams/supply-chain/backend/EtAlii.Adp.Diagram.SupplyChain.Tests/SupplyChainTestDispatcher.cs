using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.SupplyChain.Tests;

/// <summary>Routes each supply chain command to its handler, as the host's dispatcher does, so real history can run them.</summary>
internal sealed class SupplyChainTestDispatcher(ISupplyChainDocumentStore documents) : ICommandDispatcher
{
    public Task<CommandResult> DispatchAsync(ICommand command, CancellationToken cancellationToken = default) => command switch
    {
        AddSupplyChainNodeCommand add => new AddSupplyChainNodeCommandHandler(documents).ExecuteAsync(add, cancellationToken),
        ConnectSupplyChainNodesCommand connect => new ConnectSupplyChainNodesCommandHandler(documents).ExecuteAsync(connect, cancellationToken),
        RemoveSupplyChainEntryCommand remove => new RemoveSupplyChainEntryCommandHandler(documents).ExecuteAsync(remove, cancellationToken),
        PlaceSupplyChainNodesCommand place => new PlaceSupplyChainNodesCommandHandler(documents).ExecuteAsync(place, cancellationToken),
        ArrangeSupplyChainCommand arrange => new ArrangeSupplyChainCommandHandler(documents).ExecuteAsync(arrange, cancellationToken),
        SetSupplyChainPropertyCommand set => new SetSupplyChainPropertyCommandHandler(documents).ExecuteAsync(set, cancellationToken),
        StepSupplyChainValueCommand step => new StepSupplyChainValueCommandHandler(documents).ExecuteAsync(step, cancellationToken),
        GroupSupplyChainNodeCommand group => new GroupSupplyChainNodeCommandHandler(documents).ExecuteAsync(group, cancellationToken),
        RestoreDocumentCommand<ISupplyChainDocumentStore> restore => new RestoreDocumentCommandHandler<ISupplyChainDocumentStore>(documents).ExecuteAsync(restore, cancellationToken),
        _ => throw new InvalidOperationException($"No handler is registered for {command.GetType().Name}."),
    };
}
