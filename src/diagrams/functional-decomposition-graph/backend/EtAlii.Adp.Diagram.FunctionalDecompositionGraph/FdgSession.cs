using EtAlii.Adp.Documents;
using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.FunctionalDecompositionGraph;

/// <summary>
/// One connection's view of one <c>.fdg</c> document.
/// </summary>
/// <remarks>
/// <para>
/// <b>A drag is a move through the stream, not a context action</b> (the user's chat ruling of
/// 2026-09-25): the context channel carries no position, so the canvas sends the new top-left
/// through <see cref="MoveElementToAsync"/>, which runs <see cref="SetFdgPlacementCommand"/> on the
/// project's history - one undo away, like every other edit.
/// </para>
/// </remarks>
public sealed class FdgSession : DocumentDiagramSession<LineDocument, FdgModel>
{
    private readonly FdgElementMapper _mapper;

    public FdgSession(string bodyPath, IFdgDocumentStore documents, FdgElementMapper mapper, IHistoryStack? history = null)
        : base(bodyPath, documents, history)
    {
        ArgumentNullException.ThrowIfNull(mapper);

        _mapper = mapper;
    }

    /// <inheritdoc />
    protected override string ParentRefusal => "An element here has no parent to move it under; its parent is a connection, drawn from the parent to it.";

    /// <inheritdoc />
    protected override string ReadOnlyRefusal => "This graph is read-only.";

    /// <inheritdoc />
    protected override IReadOnlyList<DiagramElement> Render(FdgModel model, DiagramViewport viewport) =>
        _mapper.Visible(model, viewport);

    /// <summary>Moves an element so its top-left is (<paramref name="x"/>, <paramref name="y"/>).</summary>
    /// <remarks>
    /// The canvas sends the top-left, not the centre it drew from: it knows the element's width, and
    /// the document holds the top-left. A connection's id is refused - a curve follows its ends and
    /// has no position of its own.
    /// </remarks>
    protected override DiagramPlacement PlacementOf(FdgModel model, string elementId, double x, double y) =>
        model.ElementOf(elementId) is null
            ? DiagramPlacement.Refused("That is not something this graph can move.")
            : DiagramPlacement.By(new SetFdgPlacementCommand(BodyPath, elementId, x, y));
}
