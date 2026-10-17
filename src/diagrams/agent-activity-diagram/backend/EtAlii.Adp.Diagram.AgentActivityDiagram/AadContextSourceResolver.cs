using EtAlii.Adp.Diagram.Modules;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Hierarchy;

namespace EtAlii.Adp.Diagram.AgentActivityDiagram;

/// <summary>
/// Resolves an element or a relation of an activity file to a selection.
/// </summary>
/// <remarks>
/// <para>
/// <b>It lands with the session factory</b>: registering a session factory makes the host's
/// <c>DrawnConnections</c> guard open this module's example, and every relation it draws must
/// resolve to a selection.
/// </para>
/// <para>
/// <b>The element a selection describes is the one the canvas was sent.</b> Its payload comes from
/// <see cref="AadElementMapper"/> rather than being assembled here, so the stream and the selection
/// can never describe one element two ways.
/// </para>
/// <para>
/// <b>A placement or a finished connect gesture resolves too</b>, as the base resolves them, and so
/// do this diagram's own targets: the diagram's switch and one group of one element. Each resolves
/// to a target the action provider reads back out, and lives for one execution: never selected,
/// tracked or written.
/// </para>
/// </remarks>
public sealed class AadContextSourceResolver : DiagramElementSourceResolver<AadBody, AadModel>
{
    private readonly AadElementMapper _mapper;

    public AadContextSourceResolver(DiagramFileRouter router, IAadDocumentStore documents, AadElementMapper mapper)
        : base(router, documents)
    {
        ArgumentNullException.ThrowIfNull(mapper);
        _mapper = mapper;
    }

    /// <inheritdoc />
    protected override DiagramOrigin Origin => Diagram.AgentActivity.Origin;

    /// <inheritdoc />
    protected override DiagramElementTexts Texts { get; } = new(
        "The selected file is not an agent activity diagram.",
        "That is no longer in this diagram.",
        "New element",
        "New relation");

    /// <inheritdoc />
    protected override string? ProposedOf(AadModel model, string elementId)
    {
        if (elementId == AadElementMapper.ViewId)
        {
            return "Diagram";
        }

        return AadGroupTarget.TryParse(elementId, out var owner, out _) && model.Elements.Any(candidate => candidate.Id == owner)
            ? "Group"
            : null;
    }

    /// <inheritdoc />
    protected override DiagramElement? Drawn(AadModel model, string elementId) =>
        _mapper.Visible(model, DiagramViewport.Unbounded).FirstOrDefault(candidate => candidate.Id == elementId);

    /// <inheritdoc />
    protected override (IReadOnlyList<string> Path, string Text)? Describe(AadModel model, string elementId)
    {
        if (model.Elements.FirstOrDefault(candidate => candidate.Id == elementId) is { } element)
        {
            var text = NameOf(element);
            return ([text], text);
        }

        // A task or a pull request: a row of its element, selected by its own id.
        if (model.Elements.SelectMany(candidate => candidate.Rows).FirstOrDefault(candidate => candidate.Id == elementId) is { } row)
        {
            var text = row.Title.Length > 0 ? row.Title : row.Id;
            return ([text], text);
        }

        if (model.Relations.FirstOrDefault(candidate => candidate.Id == elementId) is { } relation)
        {
            var text = $"{NameOf(model, relation.From)} → {NameOf(model, relation.To)}";
            return ([text], text);
        }

        return null;
    }

    private static string NameOf(AadElement element) => element.Name.Length > 0 ? element.Name : element.Id;

    private static string NameOf(AadModel model, string id) =>
        model.Elements.FirstOrDefault(candidate => candidate.Id == id) is { } element ? NameOf(element) : id;
}
