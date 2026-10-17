using EtAlii.Adp.Diagram.Modules;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Hierarchy;

namespace EtAlii.Adp.Diagram.AgentBehaviorModelling;

/// <summary>
/// Resolves a node or a parent-to-child connection of a behavior model to a selection.
/// </summary>
/// <remarks>
/// <para>
/// <b>The element a selection describes is the one the canvas was sent</b>: its payload comes from
/// <see cref="AbmElementMapper"/>, so the stream and the selection never describe one node two ways.
/// </para>
/// <para>
/// <b>A drop or a finished connect gesture resolves too</b>: <c>new:x,y</c> names the node a
/// toolbox drop is about to add and <c>rel:from-&gt;to</c> the parent line a gesture proposes. Each
/// lives for one execution and is never selected, tracked or written.
/// </para>
/// </remarks>
public sealed class AbmContextSourceResolver : DiagramElementSourceResolver<AbmBody, AbmModel>
{
    private readonly AbmElementMapper _mapper;

    public AbmContextSourceResolver(DiagramFileRouter router, IAbmDocumentStore documents, AbmElementMapper mapper)
        : base(router, documents)
    {
        ArgumentNullException.ThrowIfNull(mapper);
        _mapper = mapper;
    }

    /// <inheritdoc />
    protected override DiagramOrigin Origin => Diagram.AgentBehaviorModelling.Origin;

    /// <inheritdoc />
    protected override DiagramElementTexts Texts { get; } = new(
        "The selected file is not a behavior model.",
        "That node is no longer in this behavior model.",
        "New node",
        "New parent line");

    /// <inheritdoc />
    /// <inheritdoc />
    /// <remarks>Drawn without the registration's positions: a selection says what an element is, not where.</remarks>
    protected override DiagramElement? Drawn(AbmModel model, string elementId) =>
        _mapper.Visible(model, new Dictionary<string, RegistrationPosition>(StringComparer.Ordinal), DiagramViewport.Unbounded)
            .FirstOrDefault(candidate => candidate.Id == elementId);

    /// <inheritdoc />
    protected override bool HasChildren(AbmModel model, string elementId) => model.NodeOf(elementId) is { ChildIds.Count: > 0 };

    /// <inheritdoc />
    protected override (IReadOnlyList<string> Path, string Text)? Describe(AbmModel model, string elementId)
    {
        if (model.NodeOf(elementId) is { } node)
        {
            var text = node.Label.Length > 0 ? $"{node.Keyword}: {node.Label}" : node.Keyword;
            return ([text], text);
        }

        if (elementId.StartsWith(AbmElementMapper.ChildIdPrefix, StringComparison.Ordinal)
            && model.NodeOf(elementId[AbmElementMapper.ChildIdPrefix.Length..]) is { ParentId: { } parentId } child)
        {
            var parent = model.NodeOf(parentId)!;
            var text = $"{Named(parent)} → {Named(child)}";
            return ([text], text);
        }

        return null;
    }

    private static string Named(AbmNode node) => node.Label.Length > 0 ? node.Label : node.Keyword;
}
