using EtAlii.Adp.Diagram.Modules;
using EtAlii.Adp.Hierarchy;
using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.AgentBehaviorModelling;

/// <summary>One connection's view of one behavior model.</summary>
/// <remarks>
/// <para>
/// <b>Two files feed what is drawn, and both are followed.</b> The Markdown holds the tree; the
/// registration's <c>layout:</c> block holds the positions an author dragged nodes to. A change to
/// the Markdown reaches this session through the store; a drag reaches it through the project's
/// history, because a position write goes through <see cref="ArrangeAbmNodeCommand"/>
/// and nothing watches the registration for it - the gap dotnet-dependency-graph found and closed
/// the same way.
/// </para>
/// <para>
/// <b>A drag moves a row, and may reorder it.</b> The order of a node's children is the order they
/// run in, so where a box lands across decides its place among its siblings, and the Markdown is
/// rewritten to that order (<see cref="ArrangeAbmNodeCommand"/>). Where it lands down moves its
/// whole row, with everything beneath it; that height is kept in the registration.
/// </para>
/// </remarks>
public sealed class AbmSession : DocumentDiagramSession<AbmBody, AbmModel>
{
    private readonly string? _registrationPath;
    private readonly AbmElementMapper _mapper;
    private readonly IHistoryStack? _history;

    public AbmSession(string bodyPath, string? registrationPath, IAbmDocumentStore documents, AbmElementMapper mapper, IHistoryStack? history = null)
        : base(bodyPath, documents, history)
    {
        ArgumentNullException.ThrowIfNull(mapper);

        _registrationPath = registrationPath;
        _mapper = mapper;
        _history = history;
        _history?.Changed += OnHistoryChanged;
    }

    /// <inheritdoc />
    protected override string ReadOnlyRefusal => "This behavior model is read-only.";

    /// <inheritdoc />
    protected override IReadOnlyList<DiagramElement> Render(AbmModel model, DiagramViewport viewport) =>
        _mapper.Visible(model, Stored(), viewport);

    /// <summary>Moves a node under a new parent, at <paramref name="index"/> among its children.</summary>
    protected override DiagramPlacement ParentMoveOf(string elementId, string newParentId, int index) =>
        DiagramPlacement.By(new MoveAbmNodeCommand(BodyPath, elementId, newParentId, index));

    /// <summary>
    /// A node dropped with its top-left at (<paramref name="x"/>, <paramref name="y"/>): its row moves
    /// to that height, and it takes its place among its siblings by where it landed across.
    /// </summary>
    /// <remarks>A drop that changes neither is answered with nothing to refuse and nothing written.</remarks>
    protected override DiagramPlacement PlacementOf(AbmModel model, string elementId, double x, double y)
    {
        if (_registrationPath is not { Length: > 0 } registrationPath)
        {
            return DiagramPlacement.Refused("This behavior model was opened without a registration, so there is nowhere to keep a position.");
        }

        if (model.NodeOf(elementId) is not { } node)
        {
            return DiagramPlacement.Refused("That is not something this behavior model can move.");
        }

        return AbmArrangement.Of(model, Stored(), node, x, y).IsNothing
            ? DiagramPlacement.Unchanged
            : DiagramPlacement.By(new ArrangeAbmNodeCommand(BodyPath, registrationPath, elementId, x, y));
    }

    /// <inheritdoc />
    /// <remarks>
    /// The history is the project's and outlives this session; an unremoved handler would keep a
    /// closed diagram alive and re-rendering on every command anyone runs.
    /// </remarks>
    protected override void OnDisposing() => _history?.Changed -= OnHistoryChanged;

    /// <summary>The positions the registration holds now; re-read per render, because the file is the truth.</summary>
    private IReadOnlyDictionary<string, RegistrationPosition> Stored() =>
        _registrationPath is { Length: > 0 }
            ? RegistrationLayout.Read(_registrationPath)
            : new Dictionary<string, RegistrationPosition>(StringComparer.Ordinal);

    /// <summary>
    /// A command ran, was undone or redone, somewhere in the project: push whatever the stored
    /// positions now say. Another diagram's command costs a render and an empty diff.
    /// </summary>
    private void OnHistoryChanged(object? sender, EventArgs args) => Rerender();
}
