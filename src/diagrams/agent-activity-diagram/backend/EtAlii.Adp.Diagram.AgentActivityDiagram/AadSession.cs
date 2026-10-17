using EtAlii.Adp.Diagram.Modules;
using EtAlii.Adp.Documents;
using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.AgentActivityDiagram;

/// <summary>One connection's view of one activity file: its baseline, and every change after it as deltas.</summary>
/// <remarks>
/// <b>The shared session, told what an activity file is (code-structure FR-031).</b> Following the
/// store, the baseline, the viewport and the dispatch through the project's history are the base's;
/// what is this module's is the rendering, that a drop locks an element where it is let go, and that
/// a file which can no longer be read keeps showing what was last read.
/// </remarks>
public sealed class AadSession : DocumentDiagramSession<AadBody, AadModel>
{
    private readonly AadElementMapper _mapper;
    private AadModel? _lastRead;

    public AadSession(string bodyPath, IAadDocumentStore documents, AadElementMapper mapper, IHistoryStack? history = null)
        : base(bodyPath, documents, history)
    {
        ArgumentNullException.ThrowIfNull(mapper);
        _mapper = mapper;
    }

    /// <inheritdoc />
    protected override string ParentRefusal => "An element of this diagram has no parent to move it under; it is locked where it is dropped.";

    /// <inheritdoc />
    protected override IReadOnlyList<DiagramElement> Render(AadModel model, DiagramViewport viewport) =>
        _mapper.Visible(model, viewport);

    /// <summary>
    /// A drag: the element is locked where it was let go (Requirement 6.2), through the project's
    /// history, so the lock is one undo away like every other edit.
    /// </summary>
    protected override DiagramPlacement PlacementOf(AadModel model, string elementId, double x, double y) =>
        DiagramPlacement.By(new PinAadElementCommand(BodyPath, elementId, x, y));

    /// <summary>
    /// What the file says now. A file that cannot be read says nothing, so the diagram keeps showing
    /// what it last read until the file can be read again (Requirement 8.5).
    /// </summary>
    protected override AadModel Shown(DocumentEntry<AadBody, AadModel> entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (entry.IsUsable || _lastRead is null)
        {
            _lastRead = entry.Model;
        }

        return _lastRead;
    }
}
