using EtAlii.Adp.Backend;


using EtAlii.Adp.Common;
namespace EtAlii.Adp.Diagram.DependencyGraph.Tests;

/// <summary>
/// Dispatches this module's commands to their handlers, without a container.
/// </summary>
/// <remarks>
/// A test that goes through the real history stack needs a dispatcher, and the production one
/// resolves handlers from DI. Listing them here keeps the test to this module - and a command
/// with no entry here is a command somebody added without noticing that.
/// </remarks>
internal sealed class DependencyGraphTestDispatcher(IDependencyGraphDocumentStore documents) : ICommandDispatcher
{
    public Task<CommandResult> DispatchAsync(ICommand command, CancellationToken cancellationToken = default) => command switch
    {
        SetDependencyGraphPlacementCommand placement =>
            new SetDependencyGraphPlacementCommandHandler(documents).ExecuteAsync(placement, cancellationToken),
        AddDependencyGraphElementCommand add =>
            new AddDependencyGraphElementCommandHandler(documents).ExecuteAsync(add, cancellationToken),
        RemoveDependencyGraphElementCommand remove =>
            new RemoveDependencyGraphElementCommandHandler(documents).ExecuteAsync(remove, cancellationToken),
        RestoreDependencyGraphLinesCommand restore =>
            new RestoreDependencyGraphLinesCommandHandler(documents).ExecuteAsync(restore, cancellationToken),
        RenameDependencyGraphElementCommand rename =>
            new RenameDependencyGraphElementCommandHandler(documents).ExecuteAsync(rename, cancellationToken),
        ConnectDependencyGraphElementsCommand connect =>
            new ConnectDependencyGraphElementsCommandHandler(documents).ExecuteAsync(connect, cancellationToken),
        DisconnectDependencyGraphRelationCommand disconnect =>
            new DisconnectDependencyGraphRelationCommandHandler(documents).ExecuteAsync(disconnect, cancellationToken),
        RelabelDependencyGraphRelationCommand relabel =>
            new RelabelDependencyGraphRelationCommandHandler(documents).ExecuteAsync(relabel, cancellationToken),
        AddConnectedDependencyGraphElementCommand addConnected =>
            new AddConnectedDependencyGraphElementCommandHandler(documents).ExecuteAsync(addConnected, cancellationToken),
        _ => throw new InvalidOperationException($"No handler is registered for {command.GetType().Name}."),
    };
}
