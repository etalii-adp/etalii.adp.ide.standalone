using EtAlii.Adp.Documents;
using EtAlii.Adp.History;
using EtAlii.Adp.Sessions;

namespace EtAlii.Adp.Diagram.SupplyChain;

/// <summary>One connection's view of one <c>.supply</c> document.</summary>
/// <remarks>
/// <para>
/// <b>A selection change is a change.</b> When <see cref="SupplyChainSelections"/> says this
/// connection selected something else in this body, the session re-renders with the new trace and
/// the shared diff sends exactly the elements whose marking moved.
/// </para>
/// <para>
/// <b>A drag is a move through the stream</b>, committed as one
/// <see cref="MoveSupplyChainEntryCommand"/>: a node is placed where it was dropped, and joins the
/// group whose frame it was dropped in; a group is moved by moving every member by the same
/// distance, because its frame is theirs.
/// </para>
/// </remarks>
public sealed class SupplyChainSession : DocumentDiagramSession<LineDocument, SupplyChainModel>
{
    private readonly ShortGuid _watchId;
    private readonly SupplyChainElementMapper _mapper;
    private readonly SupplyChainSelections _selections;

    public SupplyChainSession(
        ShortGuid watchId,
        string bodyPath,
        ISupplyChainDocumentStore documents,
        SupplyChainElementMapper mapper,
        SupplyChainSelections selections,
        IHistoryStack? history = null)
        : base(bodyPath, documents, history)
    {
        ArgumentNullException.ThrowIfNull(mapper);
        ArgumentNullException.ThrowIfNull(selections);

        _watchId = watchId;
        _mapper = mapper;
        _selections = selections;
        _selections.Changed += OnSelectionChanged;
    }

    /// <inheritdoc />
    protected override string ParentRefusal => "A node joins a group by being dragged inside its frame, or through its Group property.";

    /// <inheritdoc />
    protected override IReadOnlyList<DiagramElement> Render(SupplyChainModel model, DiagramViewport viewport)
    {
        var trace = SupplyChainTrace.Through(SupplyChainLayout.Of(model), _selections.Of(_watchId, BodyPath));
        return _mapper.Visible(model, viewport, trace);
    }

    /// <summary>Moves a node so its top-left is (<paramref name="x"/>, <paramref name="y"/>), or a group's frame, members and all.</summary>
    protected override DiagramPlacement PlacementOf(SupplyChainModel model, string elementId, double x, double y) =>
        DiagramPlacement.By(new MoveSupplyChainEntryCommand(BodyPath, elementId, x, y));

    /// <inheritdoc />
    protected override void OnDisposing() => _selections.Changed -= OnSelectionChanged;

    private void OnSelectionChanged(object? sender, SupplyChainSelectionChangedEventArgs args)
    {
        if (args.WatchId != _watchId || !string.Equals(args.BodyPath, BodyPath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        // Off the selecting thread: the context service raises this while it holds its own lock,
        // and a re-render writes to the delta stream, which is nothing to do while holding that.
        _ = Task.Run(Rerender);
    }
}
