using EtAlii.Adp.Documents;
using EtAlii.Adp.History;

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
public sealed class TimelineSession : DocumentDiagramSession<LineDocument, TimelineModel>
{
    private readonly TimelineElementMapper _mapper;

    /// <summary>Creates the session and subscribes to the store's changes.</summary>
    /// <param name="history">The project's history, so a drag is one undo away. Null makes the timeline read-only.</param>
    public TimelineSession(
        string bodyPath,
        ITimelineDocumentStore documents,
        TimelineElementMapper mapper,
        IHistoryStack? history = null)
        : base(bodyPath, documents, history)
    {
        ArgumentNullException.ThrowIfNull(mapper);
        _mapper = mapper;
    }

    /// <summary>A timeline element has no parent a drag could change: its place is a time and a row.</summary>
    protected override string ParentRefusal => "A timeline element has no parent to move it under; dragging changes when it happens and which row it sits on.";

    /// <inheritdoc />
    protected override string ReadOnlyRefusal => "This timeline is read-only.";

    /// <inheritdoc />
    protected override IReadOnlyList<DiagramElement> Render(TimelineModel model, DiagramViewport viewport) =>
        _mapper.Visible(model, viewport);

    /// <summary>
    /// Moves an element to a position (Requirement 6.7). The point arrives in the module's own
    /// coordinate space - seconds and row-height units - is converted back through the two
    /// shared converters, and is dispatched as one command so the drag is one undo away.
    /// </summary>
    protected override DiagramPlacement PlacementOf(TimelineModel model, string elementId, double x, double y)
    {
        ArgumentNullException.ThrowIfNull(model);

        // Also the answer for a connection's id: a curve has no position of its own - it follows
        // its endpoints (Requirement 8.6).
        if (model.Elements.FirstOrDefault(candidate => string.Equals(candidate.Id, elementId, StringComparison.Ordinal)) is not { } element)
        {
            return DiagramPlacement.Refused("That element is not something this timeline can move.");
        }

        (string begin, string? end, int row) = TimelineElementMapper.Placement(element, x, y);
        return DiagramPlacement.By(new SetTimelinePlacementCommand(BodyPath, elementId, begin, end, row));
    }
}
