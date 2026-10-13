using EtAlii.Adp.Documents;
using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.DependencyGraph;

/// <summary>
/// One open dependency graph for one connection. Core owns the stream, the authorization and the
/// connection lifetime; this owns what the graph <em>is</em> and expresses every change as a
/// <see cref="DiagramDelta"/>.
/// </summary>
/// <remarks>
/// <see cref="DocumentDiagramSession{TBody, TModel}.UpdateView"/> answers a reported viewport with
/// the difference it makes: the nodes it newly admits, then the ones it no longer does. The earlier
/// reasoning here - that a bounded graph of tens of nodes has nothing to virtualize - was a fair
/// local judgement and is superseded by view-delta-adoption, which asks the eleven modules for one
/// loop rather than eleven answers to the same question. A graph is bounded until someone points it
/// at a dependency tree that is not.
/// </remarks>
public sealed class DependencyGraphSession : DocumentDiagramSession<LineDocument, DependencyGraphModel>
{
    private readonly DependencyGraphElementMapper _mapper;

    /// <summary>Creates the session and subscribes to the store's changes.</summary>
    /// <param name="history">The project's history, so a drag is one undo away. Null makes the graph read-only.</param>
    public DependencyGraphSession(
        string bodyPath,
        IDependencyGraphDocumentStore documents,
        DependencyGraphElementMapper mapper,
        IHistoryStack? history = null)
        : base(bodyPath, documents, history)
    {
        ArgumentNullException.ThrowIfNull(mapper);
        _mapper = mapper;
    }

    /// <summary>A node has no parent a drag could change: its place is a coordinate and a row.</summary>
    protected override string ParentRefusal => "A node has no parent to move it under; dragging changes where it sits and which row it is on.";

    /// <inheritdoc />
    protected override string ReadOnlyRefusal => "This graph is read-only.";

    /// <inheritdoc />
    protected override IReadOnlyList<DiagramElement> Render(DependencyGraphModel model, DiagramViewport viewport) =>
        _mapper.Visible(model, viewport);

    /// <summary>
    /// Moves a node to a position. The point arrives in the module's own coordinate space - canvas
    /// units and row-height units - and is dispatched as one command so the drag is one undo away.
    /// </summary>
    protected override DiagramPlacement PlacementOf(DependencyGraphModel model, string elementId, double x, double y)
    {
        ArgumentNullException.ThrowIfNull(model);

        // Also the answer for a relation's id: a curve has no position of its own - it follows
        // its endpoints.
        if (DependencyGraphEdits.ElementOf(model, elementId) is null)
        {
            return DiagramPlacement.Refused("That element is not something this graph can move.");
        }

        (double movedX, int row) = DependencyGraphElementMapper.Placement(x, y);
        return DiagramPlacement.By(new SetDependencyGraphPlacementCommand(BodyPath, elementId, movedX, row));
    }
}
