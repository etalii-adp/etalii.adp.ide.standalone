using EtAlii.Adp.Documents;
using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.Sankey;

/// <summary>One connection's view of one <c>.skv</c> document.</summary>
/// <remarks>
/// <para>
/// <b>A drag is a reorder.</b> Nothing in a Sankey diagram has a position of its own, so a node
/// dropped higher or lower in its column takes its place among the others there - its entry moves
/// before the node it was dropped above - and the layout draws it there. Where across it was
/// dropped does not matter: a node's column is its flows'.
/// </para>
/// </remarks>
public sealed class SankeySession : DocumentDiagramSession<LineDocument, SankeyModel>
{
    private readonly SankeyElementMapper _mapper;

    public SankeySession(string bodyPath, ISankeyDocumentStore documents, SankeyElementMapper mapper, IHistoryStack? history = null)
        : base(bodyPath, documents, history)
    {
        ArgumentNullException.ThrowIfNull(mapper);

        _mapper = mapper;
    }

    /// <inheritdoc />
    protected override string ParentRefusal => "A node takes its place by being dragged up or down its column.";

    /// <inheritdoc />
    protected override IReadOnlyList<DiagramElement> Render(SankeyModel model, DiagramViewport viewport) =>
        _mapper.Visible(model, viewport);

    /// <summary>
    /// Takes a node dropped with its top-left at (<paramref name="x"/>, <paramref name="y"/>) to its
    /// place in its column: above the first node whose middle is below the drop's middle.
    /// </summary>
    protected override DiagramPlacement PlacementOf(SankeyModel model, string elementId, double x, double y)
    {
        _ = x;
        var layout = SankeyLayout.Of(model);
        if (!layout.Boxes.TryGetValue(elementId, out var box))
        {
            return DiagramPlacement.Refused("That is not something this diagram can move.");
        }

        // Dropped back where it was: nothing to write, and nothing to refuse.
        return PlaceOf(layout, elementId, y + (box.Height / 2)) is { } place
            ? DiagramPlacement.By(new MoveSankeyNodeCommand(BodyPath, elementId, place.Anchor, place.After))
            : DiagramPlacement.Unchanged;
    }

    /// <summary>
    /// Where a node dropped with its middle at <paramref name="centreY"/> goes: before the first
    /// other node of its column whose middle is lower, or after the last one - or <c>null</c> when
    /// that is where it already is.
    /// </summary>
    internal static (string Anchor, bool After)? PlaceOf(SankeyLayout layout, string id, double centreY)
    {
        ArgumentNullException.ThrowIfNull(layout);

        var column = layout.ColumnOrder[layout.Columns[id]];
        var others = column.Where(other => other != id).ToList();
        if (others.Count == 0)
        {
            return null;
        }

        var index = others.Count(other => layout.Boxes[other].CenterY < centreY);
        if (index == column.ToList().IndexOf(id))
        {
            return null;
        }

        return index < others.Count ? (others[index], false) : (others[^1], true);
    }
}
