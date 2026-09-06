using EtAlii.Adp.Backend;
using EtAlii.Adp.Backend.Context;
using EtAlii.Adp.Common;
using EtAlii.Adp.Common.Wire;
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
/// </remarks>
public sealed class C4ContextPropertyProvider : IContextPropertyProvider
{
    public const string NamePropertyId = "c4.name";
    public const string DescriptionPropertyId = "c4.description";
    public const string TechnologyPropertyId = "c4.technology";
    public const string TagsPropertyId = "c4.tags";
    public const string KindPropertyId = "c4.kind";
    public const string IdentifierPropertyId = "c4.identifier";

    public const string RelationshipDescriptionPropertyId = "c4.relationship.description";
    public const string RelationshipTechnologyPropertyId = "c4.relationship.technology";
    public const string RelationshipSourcePropertyId = "c4.relationship.source";
    public const string RelationshipDestinationPropertyId = "c4.relationship.destination";

    private const string Gone = "That element is no longer in this model.";

    /// <summary>Why the structural facts are shown without being editable.</summary>
    private const string StructureReason =
        "Changing this changes the shape of the model rather than a value, so it is done from the diagram rather than here.";

    private const string EndpointReason =
        "The ends of a relationship are what it is. Draw a different relationship to connect something else.";

    private readonly IHistoryStackStore _historyStacks;
    private readonly IC4DocumentStore _documents;

    public C4ContextPropertyProvider(IHistoryStackStore historyStacks, IC4DocumentStore documents)
    {
        ArgumentNullException.ThrowIfNull(historyStacks);
        ArgumentNullException.ThrowIfNull(documents);
        _historyStacks = historyStacks;
        _documents = documents;
    }

    public ContextScope Scope => ContextScope.DiagramElement;

    public ValueTask<IReadOnlyList<ContextPropertyDefinition>> DescribeAsync(ContextTarget target, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        cancellationToken.ThrowIfCancellationRequested();

        if (!TryResolve(target, out var workspace, out var element, out var relationship))
        {
            return ValueTask.FromResult<IReadOnlyList<ContextPropertyDefinition>>([]);
        }

        return ValueTask.FromResult(
            relationship is not null ? Describe(workspace, relationship) : Describe(element!));
    }

    public async ValueTask<ContextPropertyResult> SetAsync(
        ContextTarget target,
        string propertyId,
        string value,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);

        if (!TryResolve(target, out _, out var element, out var relationship))
        {
            return ContextPropertyResult.Failure(Gone);
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

        if (command is null)
        {
            return ContextPropertyResult.Failure($"'{propertyId}' is not a property of what is selected.");
        }

        var result = await _historyStacks.Get(target.RootPath).ExecuteAsync(command, cancellationToken);
        return result.IsSuccess ? ContextPropertyResult.Success : ContextPropertyResult.Failure(result.Error);
    }

    private static IReadOnlyList<ContextPropertyDefinition> Describe(C4Element element)
    {
        var properties = new List<ContextPropertyDefinition>
        {
            new(NamePropertyId, "Name", element.Name),
            new(DescriptionPropertyId, "Description", element.Description, ContextPropertyEditor.Text),
        };

        // Only what C4 gives a technology to. A person does not have one, and offering an empty
        // box for it would suggest the model is missing something it is not.
        if (element.Kind is C4ElementKind.Container or C4ElementKind.Component
            or C4ElementKind.DeploymentNode or C4ElementKind.InfrastructureNode)
        {
            properties.Add(new ContextPropertyDefinition(TechnologyPropertyId, "Technology", element.Technology));
        }

        properties.Add(new ContextPropertyDefinition(
            TagsPropertyId,
            "Tags",
            string.Join(", ", element.Tags),
            ContextPropertyEditor.Line,
            // Tags drive the palette and mark an element external, so writing them wants a
            // command of its own that can say what a tag will do. Shown meanwhile, because a
            // reader wondering why a box is grey finds the answer here.
            "Tags are edited in the document until an action exists for them."));

        properties.Add(new ContextPropertyDefinition(KindPropertyId, "Kind", Spell(element.Kind), ContextPropertyEditor.Line, StructureReason, "Model"));
        properties.Add(new ContextPropertyDefinition(IdentifierPropertyId, "Identifier", element.Id, ContextPropertyEditor.Line, StructureReason, "Model"));
        return properties;
    }

    private static IReadOnlyList<ContextPropertyDefinition> Describe(C4Workspace workspace, C4Relationship relationship) =>
    [
        new(RelationshipDescriptionPropertyId, "Description", relationship.Description),
        new(RelationshipTechnologyPropertyId, "Technology", relationship.Technology),
        new(RelationshipSourcePropertyId, "From", NameOf(workspace, relationship.SourceId), ContextPropertyEditor.Line, EndpointReason, "Model"),
        new(RelationshipDestinationPropertyId, "To", NameOf(workspace, relationship.DestinationId), ContextPropertyEditor.Line, EndpointReason, "Model"),
    ];

    /// <summary>The element's name where the model has one, and the raw identifier where it does not.</summary>
    private static string NameOf(C4Workspace workspace, string id) =>
        workspace.Find(id) is { Name.Length: > 0 } found ? found.Name : id;

    private static string Spell(C4ElementKind kind) => kind switch
    {
        C4ElementKind.SoftwareSystem => "Software system",
        C4ElementKind.DeploymentNode => "Deployment node",
        C4ElementKind.InfrastructureNode => "Infrastructure node",
        C4ElementKind.ContainerInstance => "Container instance",
        C4ElementKind.SoftwareSystemInstance => "Software system instance",
        _ => kind.ToString(),
    };

    private bool TryResolve(ContextTarget target, out C4Workspace workspace, out C4Element? element, out C4Relationship? relationship)
    {
        workspace = C4Workspace.Empty;
        element = null;
        relationship = null;

        if (target.Scope != ContextScope.DiagramElement || target.ElementId.Length == 0)
        {
            return false;
        }

        workspace = _documents.WorkspaceOf(target.ResolvedFullPath);
        element = workspace.Find(target.ElementId);
        if (element is not null)
        {
            return true;
        }

        relationship = workspace.Relationships.FirstOrDefault(candidate => candidate.Id == target.ElementId);
        return relationship is not null;
    }
}
