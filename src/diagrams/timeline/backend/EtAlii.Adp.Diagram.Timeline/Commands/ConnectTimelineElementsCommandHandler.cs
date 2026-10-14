using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.Timeline;

/// <summary>Carries out a connect: one relation entry; the first creates <c>connections:</c> at the end of the document.</summary>
public sealed class ConnectTimelineElementsCommandHandler(ITimelineDocumentStore documents)
    : TimelineEditHandler<ConnectTimelineElementsCommand>(documents)
{
    /// <inheritdoc />
    protected override CommandResult Execute(ConnectTimelineElementsCommand command) =>
        string.Equals(command.From, command.To, StringComparison.Ordinal)
            // Requirement 8.7. Refused with the reason rather than silently dropped.
            ? CommandResult.Failure("An element cannot be connected to itself.")
            : base.Execute(command);

    /// <inheritdoc />
    protected override EditOutcome Apply(TimelineBody body, TimelineModel model, ConnectTimelineElementsCommand command)
    {
        if (TimelineEdits.ElementOf(model, command.From) is null ||
            TimelineEdits.ElementOf(model, command.To) is null)
        {
            // A connection gesture that ends on nothing cancels with a reason (Requirement 8.5),
            // and writing a connection to an element that is not there would manufacture exactly
            // the dangling reference the validator exists to report.
            return EditOutcome.Refused("Both ends of a relation must be elements of this timeline.");
        }

        return TimelineEdits.AddConnection(body, command.Id, command.From, command.To, command.Label);
    }
}
