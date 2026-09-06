using EtAlii.Adp.Backend;
using EtAlii.Adp.Backend.Diagrams;
using EtAlii.Adp.Common;
using Serilog;

namespace EtAlii.Adp.Diagram.Timeline;

/// <summary>
/// One open timeline for one connection. Core owns the stream, the authorization and the
/// connection lifetime; this owns what the timeline <em>is</em> and expresses every change as a
/// <see cref="DiagramDelta"/>.
/// </summary>
/// <remarks>
/// <see cref="UpdateView"/> answers a reported viewport with the difference: what has come into
/// view is added, what has left is removed. A connection opens with everything and narrows from
/// there, so a client that never reports a view keeps exactly the behaviour this session had
/// before it filtered anything (view-delta-adoption Requirements 1.1-1.4).
/// <para>
/// This used to say the timeline had nothing to virtualize, and for a bounded diagram of tens of
/// elements that was a fair local judgement. It stopped being the code's reason when the loop
/// was adopted family-wide: the zoom range here spans seconds to years, so a reader zoomed into
/// an afternoon of a decade-long timeline is holding almost all of it off-screen.
/// </para>
/// <para>
/// What a viewport <em>means</em> stays this module's business - seconds across and rows down,
/// decided in <see cref="TimelineElementMapper.Visible"/>. Nothing about timeline units reaches
/// the shared client code, which speaks only of rectangles (Requirement 3.4).
/// </para>
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

    /// <summary>
    /// The last viewport this connection reported. Unbounded until it reports one, so a client
    /// that never reports keeps receiving the whole timeline.
    /// </summary>
    private DiagramViewport _viewport = DiagramViewport.Unbounded;

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
        _viewport = viewport;

        // The same render and the same diff a document change goes through, so the two paths
        // cannot disagree about what this connection holds. Diff emits Add for what appeared and
        // then Remove for what left - the order both reference sessions use, and the safe one: a
        // client applying Add first is never briefly missing an element it is about to be sent.
        var current = Render();
        var deltas = _mapper.Diff(_delivered, current);
        _delivered = current;

        return deltas;
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

    /// <summary>
    /// What this connection should be holding: the model, narrowed to the viewport it last
    /// reported. Unbounded until it reports one, so a baseline is the whole timeline and a
    /// client that never reports keeps receiving all of it.
    /// </summary>
    /// <remarks>
    /// Both the viewport path and the document-change path render through here, deliberately.
    /// When they did not - when a change re-rendered everything while the viewport had narrowed
    /// the view - an ordinary edit would have quietly re-sent the elements the viewport had just
    /// culled, and the two paths would have disagreed about what the client held.
    /// </remarks>
    private IReadOnlyList<DiagramElement> Render() =>
        _mapper.Visible(_documents.GetOrLoad(_bodyPath).Model, _viewport);

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
