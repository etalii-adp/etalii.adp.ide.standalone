using EtAlii.Adp.Context;
using EtAlii.Adp.Diagram.Modules;
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
/// <para>
/// What is offered depends on what is selected, and on what C4 says about it: only containers
/// and components are offered a technology, because only they have one. An action that does not
/// apply is not offered at all rather than greyed out (Requirement 13.7).
/// </para>
/// <para>
/// <b>What is offered is derived from the DISL definition</b> (c4-disl-fbl Requirement 3.1): its context
/// menus, their visibility, labels and groups, and the ids of its <c>x-c4</c> block. What each action
/// asks, what it refuses and which command it builds stays here.
/// </para>
/// <para>
/// <b>A toolbox drop is answered by the entry, not by the target's menu</b> (<see cref="IContextDropProvider"/>, the
/// user's ruling of 2026-10-19): no element menu has an add, and the definition's <c>drop.targets</c> say where each
/// entry may land and its <c>drop.refusal</c> what is said where it may not (<see cref="C4Definitions.Drop"/>).
/// </para>
/// </remarks>
public sealed class C4ContextActionProvider(IHistoryStackStore historyStacks, IC4DocumentStore documents)
    : DiagramActionProvider<LineDocument, C4Workspace>(historyStacks, documents), IContextDropProvider
{
    public const string RenameActionId = "c4.rename";
    public const string EditDescriptionActionId = "c4.edit-description";
    public const string SetTechnologyActionId = "c4.set-technology";
    public const string RelabelActionId = "c4.relabel";
    public const string SetProtocolActionId = "c4.set-protocol";

    /// <summary>The add actions the toolbox and the empty canvas name, and the operation of the definition each runs.</summary>
    private static readonly (C4ElementKind Kind, string Operation)[] AddableKinds =
    [
        (C4ElementKind.Person, "addPerson"),
        (C4ElementKind.SoftwareSystem, "addSoftwareSystem"),
        (C4ElementKind.Container, "addContainer"),
        (C4ElementKind.Component, "addComponent"),
    ];

    private const string Gone = "That element is no longer in this model.";

    /// <summary>The add action's id for <paramref name="kind"/>, as the toolbox's drop names it.</summary>
    private static string AddActionIdFor(C4ElementKind kind) => $"c4.add-{kind.ToString().ToLowerInvariant()}";

    /// <inheritdoc />
    protected override bool IsBody(string path) => path.EndsWith(Diagram.DocumentExtension, StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc />
    /// <remarks>
    /// Derived from the DISL definition of the view the target is drawn on (<see cref="C4Definitions.Menus"/>):
    /// an element's menu, a stored relationship's, the empty canvas's adds, also for a point on it; nothing for a line the view
    /// derives, for an unknown id, or for a document that could not be read, every edit of which would
    /// be refused.
    /// </remarks>
    protected override IReadOnlyList<ContextActionGroupDefinition> Discover(ContextTarget target, DocumentEntry<LineDocument, C4Workspace> entry)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(entry);
        return entry.IsUsable
            ? C4Definitions.Menus(entry.Model, C4Disl.FileOf(target.ResolvedFullPath), C4Definitions.ViewOf(entry.Model, target.Origin), target.Origin, ElementOrCanvas(target.ElementId))
            : [];
    }

    /// <summary>
    /// The element <paramref name="elementId"/> names, or the empty id of the empty canvas for a point on it
    /// (<c>new:x,y</c>): a right-click on empty canvas and a drop there name the point, and a C4 element goes
    /// at the top of the model wherever on the canvas it lands.
    /// </summary>
    private static string ElementOrCanvas(string elementId) => GestureIds.IsPlacement(elementId) ? "" : elementId;

    /// <inheritdoc />
    public async ValueTask<ContextActionDefinition?> ResolveDropAsync(ContextTarget target, string dropActionId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        cancellationToken.ThrowIfCancellationRequested();
        if (!Serves(target))
        {
            return null;
        }

        var entry = await LoadAsync(target, cancellationToken);
        return entry.IsUsable
            ? C4Definitions.Drop(entry.Model, C4Disl.FileOf(target.ResolvedFullPath), C4Definitions.ViewOf(entry.Model, target.Origin), target.Origin, ElementOrCanvas(target.ElementId), dropActionId)
            : null;
    }

    /// <inheritdoc />
    protected override ValueTask<ContextExecutionResult> RunAsync(
        ContextTarget target,
        DocumentEntry<LineDocument, C4Workspace> entry,
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
    /// C4 asks for all three, and <see cref="C4Definitions.Problems"/> reports each as a warning - but a
    /// model mid-edit is routinely incomplete, and refusing the keystroke would make the rule a
    /// blocker rather than the guidance Requirement 10.7 says it is.
    /// </remarks>
    protected override ContextValidationResult Validate(string actionId, string value) =>
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
            return AddCommand(bodyPath, model, target.Origin, adding, value, ElementOrCanvas(target.ElementId));
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
        foreach ((C4ElementKind kind, string _) in AddableKinds)
        {
            if (actionId == AddActionIdFor(kind))
            {
                return kind;
            }
        }

        return null;
    }

    /// <summary>
    /// The add <paramref name="kind"/> named <paramref name="value"/> run on <paramref name="parentId"/>, as
    /// today's command writes it. The definition's operation is run first, with the identifier the
    /// module makes from the name handed to it (<see cref="C4Placement.IdentifierFor"/>, standing in for
    /// gap C11): where it creates the element, the command declares it under that identifier, inside the
    /// parent the operation puts it in, which is none for a person or a software system dropped on another
    /// element; where it refuses, or the parent is not one it is for, the command is built as before and
    /// refuses on its own.
    /// </summary>
    private static AddC4ElementCommand AddCommand(string bodyPath, C4Workspace model, ToolOrigin? origin, C4ElementKind kind, string value, string parentId)
    {
        var identifier = C4Placement.IdentifierFor(model, value.Trim());
        var operation = AddableKinds.First(addable => addable.Kind == kind).Operation;
        var created = C4Definitions.Creation(model, C4Disl.FileOf(bodyPath), C4Definitions.ViewOf(model, origin), origin, operation, parentId, value, identifier);
        return created is { } creation && creation.Id == identifier
            ? new AddC4ElementCommand(bodyPath, kind, value, creation.ParentId is null ? "" : parentId, identifier)
            : new AddC4ElementCommand(bodyPath, kind, value, parentId);
    }

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
