using EtAlii.Adp.Context;
using EtAlii.Adp.Documents.Wire;

namespace EtAlii.Adp.Diagram.C4.Tests.Parity;

/// <summary>
/// The palette, the context menus and the property rows as the C4 module wrote them by hand before
/// they were derived from its six DISL definitions (c4-disl-fbl task 7), kept verbatim from
/// <c>C4ToolboxProvider</c>, <c>C4ContextActionProvider.Discover</c> and <c>C4ContextPropertyProvider.Describe</c>
/// as the oracle the derived ones are compared with, per document, view and element.
/// </summary>
/// <remarks>
/// The hand-written menus and rows read the parsed workspace alone: they did not depend on the view a
/// target is drawn on, nor on the type it was opened as, which is why the derived ones are compared
/// with the same answer under every view.
/// </remarks>
internal static class HandWrittenC4
{
    private const string NamePropertyId = "c4.name";
    private const string DescriptionPropertyId = "c4.description";
    private const string TechnologyPropertyId = "c4.technology";
    private const string TagsPropertyId = "c4.tags";
    private const string KindPropertyId = "c4.kind";
    private const string IdentifierPropertyId = "c4.identifier";
    private const string RelationshipDescriptionPropertyId = "c4.relationship.description";
    private const string RelationshipTechnologyPropertyId = "c4.relationship.technology";
    private const string RelationshipSourcePropertyId = "c4.relationship.source";
    private const string RelationshipDestinationPropertyId = "c4.relationship.destination";

    private const string RenameActionId = "c4.rename";
    private const string EditDescriptionActionId = "c4.edit-description";
    private const string SetTechnologyActionId = "c4.set-technology";
    private const string RelabelActionId = "c4.relabel";
    private const string SetProtocolActionId = "c4.set-protocol";

    private const string StructureReason =
        "Changing this changes the shape of the model rather than a value, so it is done from the diagram rather than here.";

    private const string EndpointReason =
        "The ends of a relationship are what it is. Draw a different relationship to connect something else.";

    /// <summary>The kinds the menus could add; the rest live in a deployment environment.</summary>
    private static readonly C4ElementKind[] AddableKinds =
    [
        C4ElementKind.Person,
        C4ElementKind.SoftwareSystem,
        C4ElementKind.Container,
        C4ElementKind.Component,
    ];

    /// <summary>The palette of the type that binds <paramref name="viewKind"/>, as the hand-written <c>C4ToolboxProvider.Items</c> listed it.</summary>
    public static IReadOnlyList<ToolboxItemDefinition> Toolbox(C4ViewKind viewKind) =>
        C4RuleSet.PermittedKinds(viewKind)
            .Select(kind => new ToolboxItemDefinition(
                $"c4.toolbox.{kind.ToString().ToLowerInvariant()}",
                Label(kind),
                IconFor(kind),
                DescriptionFor(kind, viewKind),
                $"c4.add-{kind.ToString().ToLowerInvariant()}"))
            .ToArray();

    /// <summary>The context menu of <paramref name="elementId"/>, as <c>C4ContextActionProvider.Discover</c> answered it for a usable document.</summary>
    public static IReadOnlyList<ContextActionGroupDefinition> Menus(C4Workspace workspace, string elementId)
    {
        (C4Element? element, C4Relationship? relationship) = Resolve(workspace, elementId);
        if (element is null && relationship is null)
        {
            return elementId.Length == 0
                ? [new ContextActionGroupDefinition(AddActionsFor(parent: null))]
                : [];
        }

        if (relationship is not null)
        {
            return [new ContextActionGroupDefinition([
                new ContextActionDefinition(RelabelActionId, "Relabel…", "mdi-pencil-outline", new ContextShortcutDefinition("F2")),
                new ContextActionDefinition(SetProtocolActionId, "Set technology…", "mdi-transit-connection-variant"),
            ])];
        }

        var actions = new List<ContextActionDefinition>
        {
            new(RenameActionId, "Rename…", "mdi-pencil-outline", new ContextShortcutDefinition("F2")),
            new(EditDescriptionActionId, element!.Description.Length > 0 ? "Edit description…" : "Add description…", "mdi-text-box-outline"),
        };

        if (element.Kind is C4ElementKind.Container or C4ElementKind.Component
            or C4ElementKind.DeploymentNode or C4ElementKind.InfrastructureNode)
        {
            actions.Add(new ContextActionDefinition(
                SetTechnologyActionId,
                element.Technology.Length > 0 ? "Change technology…" : "Set technology…",
                "mdi-tools"));
        }

        var adds = AddActionsFor(element);
        return adds.Count == 0
            ? [new ContextActionGroupDefinition(actions)]
            : [new ContextActionGroupDefinition(actions), new ContextActionGroupDefinition(adds)];
    }

    /// <summary>The property rows of <paramref name="elementId"/>, as <c>C4ContextPropertyProvider.Describe</c> answered them.</summary>
    public static IReadOnlyList<ContextPropertyDefinition> Rows(C4Workspace workspace, string elementId)
    {
        (C4Element? element, C4Relationship? relationship) = Resolve(workspace, elementId);
        return element is not null ? Describe(element)
            : relationship is not null ? Describe(workspace, relationship)
            : [];
    }

    private static IReadOnlyList<ContextPropertyDefinition> Describe(C4Element element)
    {
        var properties = new List<ContextPropertyDefinition>
        {
            new(NamePropertyId, "Name", element.Name),
            new(DescriptionPropertyId, "Description", element.Description, ContextPropertyEditor.Text),
        };

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

    private static string NameOf(C4Workspace workspace, string id) =>
        workspace.Find(id) is { Name.Length: > 0 } found ? found.Name : id;

    private static IReadOnlyList<ContextActionDefinition> AddActionsFor(C4Element? parent) =>
        AddableKinds
            .Where(kind => C4Placement.Refuse(C4Workspace.Empty, kind, parentId: "") is null)
            .Where(kind => FitsInside(kind, parent))
            .Select(kind => new ContextActionDefinition($"c4.add-{kind.ToString().ToLowerInvariant()}", $"Add {SpellAdd(kind).ToLowerInvariant()}&", "mdi-plus"))
            .ToArray();

    private static bool FitsInside(C4ElementKind kind, C4Element? parent) => kind switch
    {
        C4ElementKind.Person or C4ElementKind.SoftwareSystem => parent is null,
        C4ElementKind.Container => parent?.Kind == C4ElementKind.SoftwareSystem,
        C4ElementKind.Component => parent?.Kind == C4ElementKind.Container,
        _ => false,
    };

    private static string SpellAdd(C4ElementKind kind) => kind switch
    {
        C4ElementKind.SoftwareSystem => "Software system",
        _ => kind.ToString(),
    };

    private static string Spell(C4ElementKind kind) => kind switch
    {
        C4ElementKind.SoftwareSystem => "Software system",
        C4ElementKind.DeploymentNode => "Deployment node",
        C4ElementKind.InfrastructureNode => "Infrastructure node",
        C4ElementKind.ContainerInstance => "Container instance",
        C4ElementKind.SoftwareSystemInstance => "Software system instance",
        _ => kind.ToString(),
    };

    private static string Label(C4ElementKind kind) => kind switch
    {
        C4ElementKind.SoftwareSystem => "Software System",
        C4ElementKind.DeploymentNode => "Deployment Node",
        C4ElementKind.InfrastructureNode => "Infrastructure Node",
        C4ElementKind.ContainerInstance => "Container Instance",
        C4ElementKind.SoftwareSystemInstance => "Software System Instance",
        _ => kind.ToString(),
    };

    private static string IconFor(C4ElementKind kind) => kind switch
    {
        C4ElementKind.Person => "mdi-account-outline",
        C4ElementKind.SoftwareSystem or C4ElementKind.SoftwareSystemInstance => "mdi-application-outline",
        C4ElementKind.Container or C4ElementKind.ContainerInstance => "mdi-package-variant-closed",
        C4ElementKind.Component => "mdi-puzzle-outline",
        C4ElementKind.DeploymentNode => "mdi-server",
        C4ElementKind.InfrastructureNode => "mdi-lan",
        _ => "mdi-shape-outline",
    };

    private static string DescriptionFor(C4ElementKind kind, C4ViewKind viewKind) => kind switch
    {
        C4ElementKind.Container => "An application or a data store - not a Docker container.",
        C4ElementKind.Component => "A grouping of related functionality inside a container.",
        C4ElementKind.Person => "Someone who uses the software.",
        C4ElementKind.SoftwareSystem => viewKind is C4ViewKind.SystemContext
            ? "Another system this one talks to."
            : "A software system.",
        C4ElementKind.DeploymentNode => "Where things run: a server, a cluster, an execution environment.",
        C4ElementKind.InfrastructureNode => "Supporting infrastructure: DNS, a load balancer, a firewall.",
        C4ElementKind.ContainerInstance => "A container, deployed onto a node.",
        _ => Label(kind),
    };

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
