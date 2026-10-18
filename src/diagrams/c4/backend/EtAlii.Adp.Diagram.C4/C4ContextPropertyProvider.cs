using EtAlii.Adp.Context;
using EtAlii.Adp.Diagram.Modules;
using EtAlii.Adp.Documents;
using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.C4;

/// <summary>
/// What the property grid shows for a selected C4 element or relationship, and what happens
/// when one of those values is edited.
/// </summary>
/// <remarks>
/// <para>
/// The same commands the context actions use, reached a shorter way. "Rename…" and the
/// property grid's Name row are the same edit and the same undo entry; a user who prefers a
/// dialog and one who prefers a grid are not using two different features
/// (c4-diagrams Requirements 13.2, 13.3).
/// </para>
/// <para>
/// What is offered depends on what C4 says the thing has. Only a container or a component has
/// a technology, so only those are offered one - and the kind and identifier are shown but not
/// editable, because changing either is a change to the model's structure rather than to a
/// value, and belongs to an action that can think about the consequences.
/// </para>
/// <para>
/// <b>The rows are derived from the DISL definition</b> (c4-disl-fbl Requirement 3.1): its forms, their
/// visibility, values, sections and reasons, and the ids of its <c>x-c4</c> block. Which command a new
/// value is stays here.
/// </para>
/// </remarks>
public sealed class C4ContextPropertyProvider(IHistoryStackStore historyStacks, IC4DocumentStore documents)
    : DiagramPropertyProvider<LineDocument, C4Workspace>(historyStacks, documents)
{
    public const string NamePropertyId = "c4.name";
    public const string DescriptionPropertyId = "c4.description";
    public const string TechnologyPropertyId = "c4.technology";
    public const string KindPropertyId = "c4.kind";
    public const string IdentifierPropertyId = "c4.identifier";

    public const string RelationshipDescriptionPropertyId = "c4.relationship.description";
    public const string RelationshipTechnologyPropertyId = "c4.relationship.technology";
    public const string RelationshipSourcePropertyId = "c4.relationship.source";
    public const string RelationshipDestinationPropertyId = "c4.relationship.destination";

    private const string Gone = "That element is no longer in this model.";

    /// <inheritdoc />
    protected override bool IsBody(string path) => path.EndsWith(Diagram.DocumentExtension, StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc />
    /// <remarks>
    /// Derived from the DISL definition of the view the target is drawn on (<see cref="C4Definitions.Rows"/>):
    /// an element's form or a stored relationship's, nothing for anything else.
    /// </remarks>
    protected override IReadOnlyList<ContextPropertyDefinition> Describe(ContextTarget target, DocumentEntry<LineDocument, C4Workspace> entry)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(entry);
        return C4Definitions.Rows(entry.Model, C4Disl.FileOf(target.ResolvedFullPath), C4Definitions.ViewOf(entry.Model, target.Origin), target.Origin, target.ElementId);
    }

    /// <inheritdoc />
    protected override DiagramPropertyChange Change(ContextTarget target, C4Workspace model, string propertyId, string value)
    {
        ArgumentNullException.ThrowIfNull(target);

        (C4Element? element, C4Relationship? relationship) = Resolve(model, target.ElementId);
        if (element is null && relationship is null)
        {
            return DiagramPropertyChange.Refused(Gone);
        }

        var bodyPath = target.ResolvedFullPath;
        ICommand? command = propertyId switch
        {
            NamePropertyId when element is not null => new SetElementNameCommand(bodyPath, element.Id, value),
            DescriptionPropertyId when element is not null => new SetElementDescriptionCommand(bodyPath, element.Id, value),
            TechnologyPropertyId when element is not null => new SetElementTechnologyCommand(bodyPath, element.Id, value),
            RelationshipDescriptionPropertyId when relationship is not null => new SetRelationshipDescriptionCommand(bodyPath, relationship.Id, value),
            RelationshipTechnologyPropertyId when relationship is not null => new SetRelationshipTechnologyCommand(bodyPath, relationship.Id, value),
            _ => null,
        };

        return command is not null
            ? DiagramPropertyChange.By(command)
            : DiagramPropertyChange.Refused($"'{propertyId}' is not a property of what is selected.");
    }

    /// <summary>
    /// What <paramref name="elementId"/> names: an element, or a relationship, or neither. A
    /// relationship has no DSL identifier of its own, so it is looked up by the composite id the
    /// mapper puts on the wire for it.
    /// </summary>
    private static (C4Element? Element, C4Relationship? Relationship) Resolve(C4Workspace workspace, string elementId)
    {
        if (elementId.Length == 0)
        {
            return (null, null);
        }

        return workspace.Find(elementId) is { } element
            ? (element, null)
            : (null, workspace.Relationships.FirstOrDefault(candidate => candidate.Id == elementId));
    }
}
