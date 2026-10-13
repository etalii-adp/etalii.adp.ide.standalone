using EtAlii.Adp.Documents;
using EtAlii.Adp.Hierarchy;

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
public sealed class DependencyGraphContextSourceResolver : DiagramElementSourceResolver<LineDocument, DependencyGraphModel>
{
    private readonly DependencyGraphElementMapper _mapper;

    public DependencyGraphContextSourceResolver(DiagramFileRouter router, IDependencyGraphDocumentStore documents, DependencyGraphElementMapper mapper)
        : base(router, documents)
    {
        ArgumentNullException.ThrowIfNull(mapper);
        _mapper = mapper;
    }

    /// <inheritdoc />
    protected override DiagramOrigin Origin => Diagram.DependencyGraph.Origin;

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
    protected override DiagramElement? Drawn(DependencyGraphModel model, string elementId) =>
        _mapper.Elements(model).FirstOrDefault(candidate => candidate.Id == elementId);

    /// <inheritdoc />
    /// <remarks>A node's, or a dependency's, since both live on the same stream and are selected through the same channel.</remarks>
    protected override (IReadOnlyList<string> Path, string Text)? Describe(DependencyGraphModel model, string elementId)
    {
        var element = DependencyGraphEdits.ElementOf(model, elementId);
        if (element is not null)
        {
            var text = element.Label.Length > 0 ? element.Label : element.Id;
            return ([text], text);
        }

        var relation = DependencyGraphEdits.RelationOf(model, elementId);
        if (relation is not null)
        {
            var text = relation.Label.Length > 0
                ? relation.Label
                : $"{relation.From} → {relation.To}";
            return ([text], text);
        }

        return null;
    }
}
