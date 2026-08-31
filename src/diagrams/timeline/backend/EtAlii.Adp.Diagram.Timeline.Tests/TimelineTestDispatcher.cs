using EtAlii.Adp.Backend;

namespace EtAlii.Adp.Diagram.Timeline.Tests;

/// <summary>
/// Dispatches this module's commands to their handlers, without a container.
/// </summary>
/// <remarks>
/// A test that goes through the real history stack needs a dispatcher, and the production one
/// resolves handlers from DI. Listing them here keeps the test to this module - and a command
/// with no entry here is a command somebody added without noticing that.
/// </remarks>
internal sealed class TimelineTestDispatcher(ITimelineDocumentStore documents) : ICommandDispatcher
{
    public Task<CommandResult> DispatchAsync(ICommand command, CancellationToken cancellationToken = default) => command switch
    {
        SetTimelinePlacementCommand placement =>
            new SetTimelinePlacementCommandHandler(documents).ExecuteAsync(placement, cancellationToken),
        AddTimelineElementCommand add =>
            new AddTimelineElementCommandHandler(documents).ExecuteAsync(add, cancellationToken),
        RemoveTimelineElementCommand remove =>
            new RemoveTimelineElementCommandHandler(documents).ExecuteAsync(remove, cancellationToken),
        RestoreTimelineLinesCommand restore =>
            new RestoreTimelineLinesCommandHandler(documents).ExecuteAsync(restore, cancellationToken),
        RenameTimelineElementCommand rename =>
            new RenameTimelineElementCommandHandler(documents).ExecuteAsync(rename, cancellationToken),
        ConnectTimelineElementsCommand connect =>
            new ConnectTimelineElementsCommandHandler(documents).ExecuteAsync(connect, cancellationToken),
        DisconnectTimelineConnectionCommand disconnect =>
            new DisconnectTimelineConnectionCommandHandler(documents).ExecuteAsync(disconnect, cancellationToken),
        RelabelTimelineConnectionCommand relabel =>
            new RelabelTimelineConnectionCommandHandler(documents).ExecuteAsync(relabel, cancellationToken),
        _ => throw new InvalidOperationException($"No handler is registered for {command.GetType().Name}."),
    };
}
