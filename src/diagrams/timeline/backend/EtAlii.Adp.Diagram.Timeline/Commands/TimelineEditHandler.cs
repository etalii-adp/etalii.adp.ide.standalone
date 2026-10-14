using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.Timeline;

/// <summary>
/// A handler of a command that edits a timeline: the run of <see cref="TimelineEdits.Run"/>, which opens
/// the document's <see cref="TimelineBody"/>, stores the text the change left and answers with the
/// shared restore command, so an undo puts back the bytes the edit started from (timeline-disl-fbl
/// Requirement 3.6).
/// </summary>
/// <remarks>
/// A command that is refused before any document is read - an element with no id, a relation to itself -
/// overrides <see cref="Execute"/> and calls the base once it has checked.
/// </remarks>
/// <typeparam name="TCommand">The command handled.</typeparam>
public abstract class TimelineEditHandler<TCommand>(ITimelineDocumentStore documents) : ICommandHandler<TCommand>
    where TCommand : IDocumentEditCommand
{
    private readonly ITimelineDocumentStore _documents = documents ?? throw new ArgumentNullException(nameof(documents));

    /// <inheritdoc />
    public Task<CommandResult> ExecuteAsync(TCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(Execute(command));
    }

    /// <summary>Runs <paramref name="command"/> against its document.</summary>
    protected virtual CommandResult Execute(TCommand command) =>
        TimelineEdits.Run(_documents, command, (body, model) => Apply(body, model, command));

    /// <summary>
    /// Changes <paramref name="body"/>, whose model is <paramref name="model"/>, as <paramref name="command"/>
    /// asks - through <see cref="TimelineEdits"/>, as model changes - or says why not.
    /// </summary>
    protected abstract EditOutcome Apply(TimelineBody body, TimelineModel model, TCommand command);
}
