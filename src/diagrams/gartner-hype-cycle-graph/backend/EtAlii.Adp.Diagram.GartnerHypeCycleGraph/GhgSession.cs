using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.GartnerHypeCycleGraph;

/// <summary>
/// One connection's view of one <c>.ghg</c> document.
/// </summary>
/// <remarks>
/// <para>
/// <b>A drag is a move through the stream, not a context action</b> (the user's chat ruling of
/// 2026-09-25): the context channel carries no position, so the canvas sends the new top-left
/// through <see cref="MoveElementToAsync"/>, which runs <see cref="SetGhgPlacementCommand"/> on the
/// project's history - one undo away, like every other edit.
/// </para>
/// </remarks>
public sealed class GhgSession : DocumentDiagramSession<GhgBody, GhgModel>
{
    private readonly GhgElementMapper _mapper;

    public GhgSession(string bodyPath, IGhgDocumentStore documents, GhgElementMapper mapper, IHistoryStack? history = null)
        : base(bodyPath, documents, history)
    {
        ArgumentNullException.ThrowIfNull(mapper);

        _mapper = mapper;
    }

    /// <inheritdoc />
    protected override string ParentRefusal => "A trend has no parent to move it under; it is placed in time and on a row.";

    /// <inheritdoc />
    protected override string ReadOnlyRefusal => "This graph is read-only.";

    /// <inheritdoc />
    protected override IReadOnlyList<DiagramElement> Render(GhgModel model, DiagramViewport viewport) =>
        _mapper.Visible(model, viewport);

    /// <summary>Moves a trend so its top-left is (<paramref name="x"/>, <paramref name="y"/>).</summary>
    /// <remarks>
    /// The canvas sends the top-left, not the centre it drew from, as FDG's does. The command snaps the
    /// left edge to a month and the top to a row, and shifts the trend's dates and stored boundaries by
    /// the months it moved (Requirement 3.5). An influence's id is refused - a curve follows its ends
    /// and has no position of its own.
    /// </remarks>
    protected override DiagramPlacement PlacementOf(GhgModel model, string elementId, double x, double y) =>
        model.IsElement(elementId)
            ? DiagramPlacement.By(new SetGhgPlacementCommand(BodyPath, elementId, x, y))
            : DiagramPlacement.Refused("That is not something this graph can move.");
}
