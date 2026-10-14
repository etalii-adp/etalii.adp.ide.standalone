using EtAlii.Adp.Context;
using EtAlii.Adp.Documents;
using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.C4;

/// <summary>
/// What a user can do to a C4 element or relationship, offered the way every other action is -
/// so it reaches the ribbon, the right-click menu and the keyboard through one path. Each
/// action that changes the model builds a command and dispatches it through the project's
/// history; the provider never touches the document itself (c4-diagrams Requirements 13.2, 13.3).
/// </summary>
/// <remarks>
/// What is offered depends on what is selected, and on what C4 says about it: only containers
/// and components are offered a technology, because only they have one. An action that does not
/// apply is not offered at all rather than greyed out (Requirement 13.7).
/// </remarks>
public sealed class C4ContextActionProvider(IHistoryStackStore historyStacks, IC4DocumentStore documents)
    : DiagramActionProvider<C4Document, C4Workspace>(historyStacks, documents)
{
    public const string RenameActionId = "c4.rename";
    public const string EditDescriptionActionId = "c4.edit-description";
    public const string SetTechnologyActionId = "c4.set-technology";
    public const string RelabelActionId = "c4.relabel";
    public const string SetProtocolActionId = "c4.set-protocol";

    /// <summary>
    /// The add actions the toolbox names. `C4ToolboxProvider` builds these ids from the kinds a
    /// view permits, and they were dangling until now - a palette whose entries named actions
    /// nothing implemented.
    /// </summary>
    private static string AddActionIdFor(C4ElementKind kind) => $"c4.add-{kind.ToString().ToLowerInvariant()}";

    /// <summary>The kinds the toolbox can currently add; the rest live in a deployment environment.</summary>
    private static readonly C4ElementKind[] AddableKinds =
    [
        C4ElementKind.Person,
        C4ElementKind.SoftwareSystem,
        C4ElementKind.Container,
        C4ElementKind.Component,
    ];

    private const string Gone = "That element is no longer in this model.";

    /// <inheritdoc />
    protected override bool IsBody(string path) => path.EndsWith(Diagram.DocumentExtension, StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc />
    protected override IReadOnlyList<ContextActionGroupDefinition> Discover(DocumentEntry<C4Document, C4Workspace> entry, string elementId)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (!entry.IsUsable)
        {
            // A document that could not be read offers nothing: every edit of it would be refused.
            return [];
        }

        (C4Element? element, C4Relationship? relationship) = Resolve(entry.Model, elementId);
        if (element is null && relationship is null)
        {
            // No element: the diagram itself. Only the adds that need no parent apply.
            return elementId.Length == 0
                ? [new ContextActionGroupDefinition(AddActionsFor(parent: null))]
                : [];
        }

        if (relationship is not null)
        {
            // C4 requires a relationship to be labelled, and a protocol where it crosses a
            // boundary - so those are the two things there are to do to one.
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

        // Only containers and components have a technology in C4; offering one on a person
        // would invite an edit that the command then has to refuse.
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

    /// <inheritdoc />
    protected override ValueTask<ContextExecutionResult> RunAsync(
        ContextTarget target,
        DocumentEntry<C4Document, C4Workspace> entry,
        string actionId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(entry);

        if (KindOfAdd(actionId) is { } adding)
        {
            // Asked before anything is written, so the element arrives named rather than as
            // "New container" for the user to find and rename.
            return Result(new ContextExecutionRequiresInput(new ContextInputRequest(
                $"Add {Spell(adding).ToLowerInvariant()}",
                "mdi-plus",
                "Name",
                "",
                "Add")));
        }

        (C4Element? element, C4Relationship? relationship) = Resolve(entry.Model, target.ElementId);
        if (element is null && relationship is null)
        {
            return Result(new ContextExecutionFailed(Gone));
        }

        // Every one of these asks for text first; the commit below turns the answer into a
        // command. The current value is the initial one, so an edit starts from what is there.
        return actionId switch
        {
            // The element's name is the text drawn on its box, so this prompt carries the
            // element id and may be rendered in place of that name.
            RenameActionId when element is not null => Result(new ContextExecutionRequiresInput(
                new ContextInputRequest("Rename element", "mdi-pencil-outline", "Name", element.Name, "Rename", target.ElementId))),

            EditDescriptionActionId when element is not null => Result(new ContextExecutionRequiresInput(
                new ContextInputRequest("Describe element", "mdi-text-box-outline", "Description", element.Description, "Save"))),

            SetTechnologyActionId when element is not null => Result(new ContextExecutionRequiresInput(
                new ContextInputRequest("Set technology", "mdi-tools", "Technology", element.Technology, "Save"))),

            // A relationship's description is the authored half of what is drawn on the arrow.
            // The canvas renders it as "description [technology]", sometimes numbered, and the
            // editor replaces that whole rendered label while editing this one value - the
            // decoration around it is chrome, and the technology has its own action below.
            RelabelActionId when relationship is not null => Result(new ContextExecutionRequiresInput(
                new ContextInputRequest("Relabel relationship", "mdi-pencil-outline", "Description", relationship.Description, "Save", target.ElementId))),

            SetProtocolActionId when relationship is not null => Result(new ContextExecutionRequiresInput(
                new ContextInputRequest("Set technology", "mdi-transit-connection-variant", "Technology", relationship.Technology, "Save"))),

            _ => Result(new ContextExecutionFailed($"'{actionId}' does not apply to this selection.")),
        };
    }

    /// <summary>
    /// Judges the typed value as it is typed. Only a name is refused when empty: an element
    /// with no name leaves nothing to identify it by, on the diagram or in the document.
    /// </summary>
    /// <remarks>
    /// A missing description, technology or relationship label is deliberately *accepted* here.
    /// C4 asks for all three, and <see cref="C4RuleSet"/> reports each as a warning - but a
    /// model mid-edit is routinely incomplete, and refusing the keystroke would make the rule a
    /// blocker rather than the guidance Requirement 10.7 says it is.
    /// </remarks>
    protected override ContextValidationResult Validate(ContextTarget target, string actionId, string value) =>
        (actionId == RenameActionId || KindOfAdd(actionId) is not null) && string.IsNullOrWhiteSpace(value)
            ? ContextValidationResult.Rejected("Every C4 element needs a name.")
            : ContextValidationResult.Accepted;

    /// <inheritdoc />
    protected override ICommand? CommitCommand(ContextTarget target, C4Workspace model, string actionId, string value)
    {
        ArgumentNullException.ThrowIfNull(target);
        var bodyPath = target.ResolvedFullPath;

        // The parent is whatever the element was dropped on - a software system for a
        // container, a container for a component, and nothing at all for a drop on empty
        // canvas. The command judges whether that pairing is legal and says so if not.
        if (KindOfAdd(actionId) is { } adding)
        {
            return new AddC4ElementCommand(bodyPath, adding, value, target.ElementId);
        }

        (C4Element? element, C4Relationship? relationship) = Resolve(model, target.ElementId);
        return actionId switch
        {
            RenameActionId when element is not null => new SetElementNameCommand(bodyPath, element.Id, value),
            EditDescriptionActionId when element is not null => new SetElementDescriptionCommand(bodyPath, element.Id, value),
            SetTechnologyActionId when element is not null => new SetElementTechnologyCommand(bodyPath, element.Id, value),
            RelabelActionId when relationship is not null => new SetRelationshipDescriptionCommand(bodyPath, relationship.Id, value),
            SetProtocolActionId when relationship is not null => new SetRelationshipTechnologyCommand(bodyPath, relationship.Id, value),
            _ => null,
        };
    }

    /// <summary>The kind an add action adds, or null when the id is not an add.</summary>
    private static C4ElementKind? KindOfAdd(string actionId)
    {
        foreach (var kind in AddableKinds)
        {
            if (actionId == AddActionIdFor(kind))
            {
                return kind;
            }
        }

        return null;
    }

    /// <summary>What may be added inside <paramref name="parent"/> - or at the top, when it is null.</summary>
    private static IReadOnlyList<ContextActionDefinition> AddActionsFor(C4Element? parent) =>
        AddableKinds
            .Where(kind => C4Placement.Refuse(C4Workspace.Empty, kind, parentId: "") is null)
            .Where(kind => FitsInside(kind, parent))
            .Select(kind => new ContextActionDefinition(AddActionIdFor(kind), $"Add {Spell(kind).ToLowerInvariant()}&", "mdi-plus"))
            .ToArray();

    /// <summary>C4's containment rule, as the menu needs it: what belongs inside what.</summary>
    private static bool FitsInside(C4ElementKind kind, C4Element? parent) => kind switch
    {
        C4ElementKind.Person or C4ElementKind.SoftwareSystem => parent is null,
        C4ElementKind.Container => parent?.Kind == C4ElementKind.SoftwareSystem,
        C4ElementKind.Component => parent?.Kind == C4ElementKind.Container,
        _ => false,
    };

    private static string Spell(C4ElementKind kind) => kind switch
    {
        C4ElementKind.SoftwareSystem => "Software system",
        _ => kind.ToString(),
    };

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

    private static ValueTask<ContextExecutionResult> Result(ContextExecutionResult result) =>
        ValueTask.FromResult(result);
}
