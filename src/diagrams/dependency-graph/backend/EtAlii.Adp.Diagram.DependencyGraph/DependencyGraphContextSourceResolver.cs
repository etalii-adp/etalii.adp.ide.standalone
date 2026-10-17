using EtAlii.Adp.Diagram.Modules;
using EtAlii.Adp.Hierarchy;
using EtAlii.Adp.Specification.Disl;

namespace EtAlii.Adp.Diagram.DependencyGraph;

/// <summary>
/// Makes a node - or a dependency - selectable: resolves an <c>element_id</c> against the document
/// named by the enclosing level and verifies the id really is in it.
/// </summary>
/// <remarks>
/// Every diagram type answers for <c>element_id</c>, and none can tell from the id alone
/// whether the element is one of its own - only the file above it says that. So this refuses a
/// file that is not a <c>.dgr</c> and the selection resolver moves on to the next resolver,
/// which is what lets every module's element resolver coexist.
/// </remarks>
public sealed class DependencyGraphContextSourceResolver : DiagramElementSourceResolver<DependencyGraphBody, DislModel>
{
    private readonly DependencyGraphElementMapper _mapper;

    public DependencyGraphContextSourceResolver(DiagramFileRouter router, IDependencyGraphDocumentStore documents, DependencyGraphElementMapper mapper)
        : base(router, documents)
    {
        ArgumentNullException.ThrowIfNull(mapper);
        _mapper = mapper;
    }

    /// <inheritdoc />
    protected override ToolOrigin Origin => Diagram.DependencyGraph.Origin;

    /// <inheritdoc />
    /// <remarks>A drop and a drawn relation are both proposed as the node about to exist.</remarks>
    protected override DiagramElementTexts Texts { get; } = new(
        "The selected file is not a dependency graph.",
        "That element is no longer in this graph.",
        "New node",
        "New node");

    /// <inheritdoc />
    /// <remarks>
    /// The same payload the canvas already has, taken from the mapper rather than assembled here,
    /// so the selection and the stream can never describe one element two ways.
    /// </remarks>
    protected override DiagramElement? Drawn(DislModel model, string elementId) =>
        _mapper.Elements(model).FirstOrDefault(candidate => candidate.Id == elementId);

    /// <inheritdoc />
    /// <remarks>A node's, or a dependency's, since both live on the same stream and are selected through the same channel.</remarks>
    protected override (IReadOnlyList<string> Path, string Text)? Describe(DislModel model, string elementId)
    {
        var element = DependencyGraphEdits.NodeOf(model, elementId);
        if (element is not null)
        {
            var label = element.ValueOf("label") as string ?? "";
            var text = label.Length > 0 ? label : elementId;
            return ([text], text);
        }

        var relation = DependencyGraphEdits.RelationOf(model, elementId);
        if (relation is not null)
        {
            var label = relation.ValueOf("label") as string ?? "";
            var text = label.Length > 0
                ? label
                : $"{DependencyGraphDefinition.HostText(relation, "writtenFrom")} → {DependencyGraphDefinition.HostText(relation, "writtenTo")}";
            return ([text], text);
        }

        return null;
    }
}
