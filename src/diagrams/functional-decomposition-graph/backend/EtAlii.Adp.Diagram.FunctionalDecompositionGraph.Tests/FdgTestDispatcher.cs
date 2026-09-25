using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.FunctionalDecompositionGraph.Tests;

/// <summary>Routes each FDG command to its handler, as the host's dispatcher does, so real history can run them.</summary>
internal sealed class FdgTestDispatcher(IFdgDocumentStore documents) : ICommandDispatcher
{
    public Task<CommandResult> DispatchAsync(ICommand command, CancellationToken cancellationToken = default) => command switch
    {
        AddFdgElementCommand add => new AddFdgElementCommandHandler(documents).ExecuteAsync(add, cancellationToken),
        RemoveFdgElementCommand remove => new RemoveFdgElementCommandHandler(documents).ExecuteAsync(remove, cancellationToken),
        SetFdgPlacementCommand move => new SetFdgPlacementCommandHandler(documents).ExecuteAsync(move, cancellationToken),
        SetFdgSizeCommand size => new SetFdgSizeCommandHandler(documents).ExecuteAsync(size, cancellationToken),
        ConnectFdgElementsCommand connect => new ConnectFdgElementsCommandHandler(documents).ExecuteAsync(connect, cancellationToken),
        DisconnectFdgConnectionCommand disconnect => new DisconnectFdgConnectionCommandHandler(documents).ExecuteAsync(disconnect, cancellationToken),
        RenameFdgElementCommand rename => new RenameFdgElementCommandHandler(documents).ExecuteAsync(rename, cancellationToken),
        SetFdgDescriptionCommand describe => new SetFdgDescriptionCommandHandler(documents).ExecuteAsync(describe, cancellationToken),
        RenameFdgConnectionCommand renameConnection => new RenameFdgConnectionCommandHandler(documents).ExecuteAsync(renameConnection, cancellationToken),
        RestoreDocumentCommand<IFdgDocumentStore> restore => new RestoreDocumentCommandHandler<IFdgDocumentStore>(documents).ExecuteAsync(restore, cancellationToken),
        _ => throw new InvalidOperationException($"No handler is registered for {command.GetType().Name}."),
    };
}
