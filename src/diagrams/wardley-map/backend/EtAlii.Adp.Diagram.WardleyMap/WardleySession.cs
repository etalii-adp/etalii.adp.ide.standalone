using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.WardleyMap;

/// <summary>
/// One open Wardley map for one connection (Requirement 10.8).
/// </summary>
/// <remarks>
/// <para>
/// Following the store, the baseline, the viewport answers, the diff and the dispatch of a drag
/// are the shared session's (code-structure FR-031); what is the map's own is what it draws, the
/// pipeline groups its baseline carries, and how a drop becomes a position on the map's axes.
/// </para>
/// <para>
/// <b>It filters by viewport</b> (view-delta-adoption Requirement 1). The viewport arrives in the
/// map's own 0..1 space, which is this module's unit and stays its business; the shared client code
/// converts nothing (Requirement 3.4). What is visible is <see cref="WardleyElementMapper.Visible"/>'s
/// decision.
/// </para>
/// <para>
/// <b>An element's id is the store's identity for it</b>, kept per document beside the map, so
/// what is drawn is read with the identities of this session's document.
/// </para>
/// </remarks>
public sealed class WardleySession : DocumentDiagramSession<WardleyDocument, WardleyMap>
{
    private readonly IWardleyDocumentStore _documents;
    private readonly WardleyElementMapper _mapper;

    /// <param name="bodyPath">The map this session shows.</param>
    /// <param name="documents">The store every Wardley session shares.</param>
    /// <param name="mapper">What the map looks like on the canvas.</param>
    /// <param name="history">The project's history, so a drag is one undo away. Null makes the map read-only.</param>
    public WardleySession(
        string bodyPath,
        IWardleyDocumentStore documents,
        WardleyElementMapper mapper,
        IHistoryStack? history = null)
        : base(bodyPath, documents, history)
    {
        ArgumentNullException.ThrowIfNull(mapper);

        _documents = documents;
        _mapper = mapper;
    }

    /// <inheritdoc />
    protected override string ParentRefusal => "Dragging a component changes where it sits on the map, not what contains it.";

    /// <inheritdoc />
    protected override string ReadOnlyRefusal => "This map is read-only.";

    /// <inheritdoc />
    protected override IReadOnlyList<DiagramElement> Render(WardleyMap model, DiagramViewport viewport) =>
        _mapper.Visible(model, _documents.Identities(BodyPath), viewport);

    /// <summary>One group per pipeline, sent once with the baseline (Requirement 10.5).</summary>
    protected override IReadOnlyList<DiagramDelta> Groups(WardleyMap model) =>
        _mapper.Group(model, _documents.Identities(BodyPath));

    /// <summary>
    /// A drop moves the element to a position (Requirement 7.2). The canvas point is converted back
    /// into the document's own axes here, through the one function that may do it; the command's
    /// handler clamps it to the map.
    /// </summary>
    protected override DiagramPlacement PlacementOf(WardleyMap model, string elementId, double x, double y)
    {
        var coordinate = WardleyAxis.ToCoordinate(x, y);
        return DiagramPlacement.By(new MoveWardleyElementCommand(BodyPath, elementId, coordinate.Visibility, coordinate.Maturity));
    }
}
