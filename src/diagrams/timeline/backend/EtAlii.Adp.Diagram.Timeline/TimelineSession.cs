using EtAlii.Adp.Backend;
using EtAlii.Adp.Backend.Diagrams;
using Serilog;

namespace EtAlii.Adp.Diagram.Timeline;

/// <summary>
/// One open timeline for one connection. Core owns the stream, the authorization and the
/// connection lifetime; this owns what the timeline <em>is</em> and expresses every change as a
/// <see cref="DiagramDelta"/>.
/// </summary>
/// <remarks>
/// It filters nothing: <see cref="UpdateView"/> answers with nothing new because the whole
/// timeline is delivered at open, on the same reasoning the Wardley session spells out - a
/// bounded diagram of tens of elements has nothing to virtualize, and a filter written only to
/// satisfy a rule is code nobody needs. Panning and zooming are the client's own transform and
/// never reach here (Requirement 5).
/// </remarks>
public sealed class TimelineSession : IDiagramSession
{
    private static readonly ILogger _logger = Log.ForContext<TimelineSession>();

    private readonly string _bodyPath;
    private readonly ITimelineDocumentStore _documents;
    private readonly TimelineElementMapper _mapper;

    /// <summary>The project's history, so a drag is one undo away. Null makes the timeline read-only.</summary>
    private readonly IHistoryStack? _history;

    /// <summary>What this connection was last sent, so a change can be diffed against it.</summary>
    private IReadOnlyList<DiagramElement> _delivered = [];

    /// <summary>Creates the session and subscribes to the store's changes.</summary>
    public TimelineSession(
        string bodyPath,
        ITimelineDocumentStore documents,
        TimelineElementMapper mapper,
        IHistoryStack? history = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bodyPath);
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(mapper);

        _bodyPath = bodyPath;
        _documents = documents;
        _mapper = mapper;
        _history = history;
        _documents.Changed += OnDocumentChanged;
    }

    /// <inheritdoc />
    public event EventHandler<DiagramDeltasEventArgs>? Changed;

    /// <inheritdoc />
    public IReadOnlyList<DiagramDelta> Baseline()
    {
        var elements = Render();
        _delivered = elements;

        return elements.Count > 0
            ? [new DiagramAddDelta(elements)]
            : [];
    }

    /// <inheritdoc />
    public IReadOnlyList<DiagramDelta> UpdateView(DiagramViewport viewport)
    {
        _ = viewport;

        // Nothing changes with the viewport: the connection already holds the whole timeline,
        // and the view transform is the client's own (see the class remarks).
        return [];
    }

    /// <summary>
    /// Refused. A timeline element has no parent a drag could change - its place is a time and
    /// a row, which is <see cref="MoveElementToAsync"/>.
    /// </summary>
    public Task<string> MoveElementAsync(string elementId, string newParentId, int index, CancellationToken cancellationToken)
    {
        _ = elementId;
        _ = newParentId;
        _ = index;
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult("A timeline element has no parent to move it under; dragging changes when it happens and which row it sits on.");
    }

    /// <summary>
    /// Moves an element to a position (Requirement 6.7). The point arrives in the module's own
    /// coordinate space - seconds and row-height units - is converted back through the two
    /// shared converters, and is dispatched as one command so the drag is one undo away.
    /// </summary>
    public async Task<string> MoveElementToAsync(string elementId, double x, double y, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(elementId);
        cancellationToken.ThrowIfCancellationRequested();

        if (_history is null)
        {
            return "This timeline is read-only.";
        }

        var entry = _documents.GetOrLoad(_bodyPath);
        var element = entry.Model.Elements.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, elementId, StringComparison.Ordinal));
        if (element is null)
        {
            // Also the answer for a connection's id: a curve has no position of its own - it
            // follows its endpoints (Requirement 8.6).
            return "That element is not something this timeline can move.";
        }

        var (begin, end, row) = TimelineElementMapper.Placement(element, x, y);
        var result = await _history.ExecuteAsync(
            new SetTimelinePlacementCommand(_bodyPath, elementId, begin, end, row, "Moved"),
            cancellationToken);

        return result.IsSuccess ? "" : result.Error;
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        _documents.Changed -= OnDocumentChanged;
        return ValueTask.CompletedTask;
    }

    private IReadOnlyList<DiagramElement> Render() =>
        _mapper.Elements(_documents.GetOrLoad(_bodyPath).Model);

    /// <summary>
    /// A save from any connection, or an edit made outside ADP, arrives here and goes out as
    /// the difference from what this connection was last sent.
    /// </summary>
    private void OnDocumentChanged(object? sender, TimelineDocumentChangedEventArgs args)
    {
        if (!string.Equals(args.Path, _bodyPath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        try
        {
            var current = Render();
            var deltas = _mapper.Diff(_delivered, current);
            _delivered = current;

            if (deltas.Count > 0)
            {
                Changed?.Invoke(this, new DiagramDeltasEventArgs(deltas));
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A file that vanished or locked mid-reload costs this notification, never the
            // session: the next change tries again.
            _logger.Warning(exception, "Could not re-read {BodyPath} after a change", _bodyPath);
        }
    }
}
