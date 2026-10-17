using EtAlii.Adp.Diagram.Modules;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Hierarchy;

namespace EtAlii.Adp.Diagram.C4;

/// <summary>
/// Makes a C4 element or relationship selectable: resolves an <c>element_id</c> against the model
/// named by the enclosing selection level, and verifies it really is in it (c4-diagrams
/// Requirement 13.2).
/// </summary>
/// <remarks>
/// <para>
/// <b>The shared resolver, told what this diagram's ids mean</b> (code-structure FR-032): the
/// checks, the gestures, the payload and the following are the base's. One is registered per C4
/// type, as each type's session factory is, because a selection is answered by the resolver of the
/// file's own origin.
/// </para>
/// <para>
/// <b>What an element looks like does not depend on the view, but where a relationship is drawn
/// does.</b> The payload is taken from the first view that draws the id, laid out as the
/// document says, so an element is described exactly as the canvas was sent it, and a line that
/// only one view elevates is still found. The resolver does not know which view the reader has
/// open, so a relationship's end coordinates are those of that first view's computed layout.
/// </para>
/// </remarks>
public sealed class C4ContextSourceResolver : DiagramElementSourceResolver<LineDocument, C4Workspace>
{
    private readonly C4ElementMapper _mapper;

    /// <summary>Creates the resolver of the C4 type at <paramref name="origin"/>, over the router and the one document store.</summary>
    public C4ContextSourceResolver(ToolOrigin origin, DiagramFileRouter router, IC4DocumentStore documents, C4ElementMapper mapper)
        : base(router, documents)
    {
        ArgumentNullException.ThrowIfNull(origin);
        ArgumentNullException.ThrowIfNull(mapper);
        Origin = origin;
        _mapper = mapper;
    }

    /// <inheritdoc />
    protected override ToolOrigin Origin { get; }

    /// <inheritdoc />
    protected override DiagramElementTexts Texts { get; } = new(
        "The selected file is not a C4 diagram.",
        "That element is no longer in this diagram.",
        "New element",
        "New relationship");

    /// <summary>
    /// An element reads as its chain of names from the top of the model, which is what identifies it
    /// to a human; a relationship carries no name of its own, and reads as its two ends.
    /// </summary>
    protected override (IReadOnlyList<string> Path, string Text)? Describe(C4Workspace model, string elementId)
    {
        ArgumentNullException.ThrowIfNull(model);

        if (model.Find(elementId) is { } element)
        {
            return (PathOf(model, element), element.Name);
        }

        return RelationshipDrawnAs(model, elementId) is { } relationship
            ? ([], RelationshipText(model, relationship))
            : null;
    }

    /// <inheritdoc />
    protected override bool HasChildren(C4Workspace model, string elementId)
    {
        ArgumentNullException.ThrowIfNull(model);
        return model.Elements.Any(candidate => string.Equals(candidate.ParentId, elementId, StringComparison.OrdinalIgnoreCase));
    }

    /// <inheritdoc />
    protected override DiagramElement? Drawn(C4Workspace model, string elementId)
    {
        ArgumentNullException.ThrowIfNull(model);
        return model.Views
            .Select(view => _mapper.Visible(model, view, DiagramViewport.Unbounded).FirstOrDefault(candidate => candidate.Id == elementId))
            .FirstOrDefault(drawn => drawn is not null);
    }

    /// <summary>The element's chain of names from the top of the model - what identifies it to a human.</summary>
    private static IReadOnlyList<string> PathOf(C4Workspace workspace, C4Element element)
    {
        var names = new List<string>();
        var current = element;
        while (current is not null)
        {
            names.Insert(0, current.Name.Length > 0 ? current.Name : current.Id);
            current = current.ParentId is { } parentId ? workspace.Find(parentId) : null;
        }

        return names;
    }

    /// <summary>
    /// The relationship a drawn relationship id names - the model's own, or one the view ELEVATED: a
    /// view that does not show an end draws the line to that end's nearest shown ancestor (see
    /// <c>C4ElementMapper.RelationshipsOf</c>), so a component view draws <c>spa -> api</c> as
    /// <c>spa->tracking@88</c>. Its id is then in no model relationship, and a lookup of the model's alone
    /// refused every elevated line, so none could be selected (centralized-selection task 26). Returned
    /// with the drawn ends, so the detail names what the user pressed.
    /// </summary>
    private static C4Relationship? RelationshipDrawnAs(C4Workspace workspace, string elementId)
    {
        if (workspace.Relationships.FirstOrDefault(candidate => candidate.Id == elementId) is { } exact)
        {
            return exact;
        }

        var at = elementId.LastIndexOf('@');
        var arrow = elementId.IndexOf("->", StringComparison.Ordinal);
        if (arrow <= 0 || at < arrow + 2 || !uint.TryParse(elementId[(at + 1)..], out var line))
        {
            return null;
        }

        var from = elementId[..arrow];
        var to = elementId[(arrow + 2)..at];
        var elevated = workspace.Relationships.FirstOrDefault(candidate =>
            candidate.Line == line &&
            (IsSelfOrAncestor(workspace, from, candidate.SourceId) || IsInstanceOf(workspace, from, candidate.SourceId)) &&
            (IsSelfOrAncestor(workspace, to, candidate.DestinationId) || IsInstanceOf(workspace, to, candidate.DestinationId)));
        if (elevated is not null)
        {
            return elevated with { SourceId = from, DestinationId = to };
        }

        // A dynamic view draws its own interactions, which are declared in the view rather than
        // the model, at the interaction's line.
        var interaction = workspace.Views
            .SelectMany(view => view.Interactions)
            .FirstOrDefault(candidate =>
                candidate.Line == line &&
                string.Equals(candidate.SourceId, from, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(candidate.DestinationId, to, StringComparison.OrdinalIgnoreCase));
        return interaction is null
            ? null
            : new C4Relationship(interaction.SourceId, interaction.DestinationId, interaction.Description, "", interaction.Line);
    }

    /// <summary>
    /// Whether <paramref name="candidate"/> is an instance of <paramref name="id"/>: a deployment
    /// view draws a relationship between two containers between their instances.
    /// </summary>
    private static bool IsInstanceOf(C4Workspace workspace, string candidate, string id) =>
        workspace.Find(candidate) is { ReferencedId: { } referencedId }
        && string.Equals(referencedId, id, StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether <paramref name="candidate"/> is <paramref name="id"/> or one of the elements it sits inside.</summary>
    private static bool IsSelfOrAncestor(C4Workspace workspace, string candidate, string id)
    {
        for (var current = workspace.Find(id); current is not null; current = current.ParentId is { } parentId ? workspace.Find(parentId) : null)
        {
            if (string.Equals(current.Id, candidate, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>An end's name as drawn: an instance goes by the element it instantiates.</summary>
    private static string NameOf(C4Workspace workspace, string id) =>
        workspace.Find(id) is { } element ? C4LayoutEngine.Displayed(workspace, element).Name : id;

    private static string RelationshipText(C4Workspace workspace, C4Relationship relationship)
    {
        var source = NameOf(workspace, relationship.SourceId);
        var destination = NameOf(workspace, relationship.DestinationId);
        return relationship.Description.Length > 0
            ? $"{source} -> {destination}: {relationship.Description}"
            : $"{source} -> {destination}";
    }
}
