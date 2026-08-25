using EtAlii.Adp.Backend;
using EtAlii.Adp.Backend.Context;

namespace EtAlii.Adp.C4;

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
public sealed class C4ContextActionProvider : IContextActionProvider
{
    public const string RenameActionId = "c4.rename";
    public const string EditDescriptionActionId = "c4.edit-description";
    public const string SetTechnologyActionId = "c4.set-technology";
    public const string RelabelActionId = "c4.relabel";
    public const string SetProtocolActionId = "c4.set-protocol";

    private const string Gone = "That element is no longer in this model.";

    private readonly IHistoryStackStore _historyStacks;
    private readonly IC4DocumentStore _documents;

    public C4ContextActionProvider(IHistoryStackStore historyStacks, IC4DocumentStore documents)
    {
        ArgumentNullException.ThrowIfNull(historyStacks);
        ArgumentNullException.ThrowIfNull(documents);
        _historyStacks = historyStacks;
        _documents = documents;
    }

    public ContextScope Scope => ContextScope.DiagramElement;

    public ValueTask<IReadOnlyList<ContextActionGroupDefinition>> DiscoverAsync(ContextTarget target, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        cancellationToken.ThrowIfCancellationRequested();

        if (!TryResolve(target, out var workspace, out var element, out var relationship))
        {
            return Empty();
        }

        if (relationship is not null)
        {
            // C4 requires a relationship to be labelled, and a protocol where it crosses a
            // boundary - so those are the two things there are to do to one.
            return Groups(new ContextActionGroupDefinition([
                new ContextActionDefinition(RelabelActionId, "Relabel…", "mdi-pencil-outline", new ContextShortcutDefinition("F2")),
                new ContextActionDefinition(SetProtocolActionId, "Set technology…", "mdi-transit-connection-variant"),
            ]));
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

        _ = workspace;
        return Groups(new ContextActionGroupDefinition(actions));
    }

    public ValueTask<ContextExecutionResult> ExecuteAsync(ContextTarget target, string actionId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        cancellationToken.ThrowIfCancellationRequested();

        if (!TryResolve(target, out _, out var element, out var relationship))
        {
            return Result(new ContextExecutionFailed(Gone));
        }

        // Every one of these asks for text first; the commit below turns the answer into a
        // command. The current value is the initial one, so an edit starts from what is there.
        return actionId switch
        {
            RenameActionId when element is not null => Result(new ContextExecutionRequiresInput(
                new ContextInputRequest("Rename element", "mdi-pencil-outline", "Name", element.Name, "Rename"))),

            EditDescriptionActionId when element is not null => Result(new ContextExecutionRequiresInput(
                new ContextInputRequest("Describe element", "mdi-text-box-outline", "Description", element.Description, "Save"))),

            SetTechnologyActionId when element is not null => Result(new ContextExecutionRequiresInput(
                new ContextInputRequest("Set technology", "mdi-tools", "Technology", element.Technology, "Save"))),

            RelabelActionId when relationship is not null => Result(new ContextExecutionRequiresInput(
                new ContextInputRequest("Relabel relationship", "mdi-pencil-outline", "Description", relationship.Description, "Save"))),

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
    public ValueTask<ContextValidationResult> ValidateAsync(ContextTarget target, string actionId, string value, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _ = target;

        if (actionId == RenameActionId && string.IsNullOrWhiteSpace(value))
        {
            return ValueTask.FromResult(ContextValidationResult.Rejected("Every C4 element needs a name."));
        }

        return ValueTask.FromResult(ContextValidationResult.Accepted);
    }

    public async ValueTask<ContextCommitResult> CommitAsync(ContextTarget target, string actionId, string value, string text, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);

        if (!TryResolve(target, out _, out var element, out var relationship))
        {
            return ContextCommitResult.Failed(Gone);
        }

        var bodyPath = target.ResolvedFullPath;
        ICommand? command = actionId switch
        {
            RenameActionId when element is not null => new SetElementNameCommand(bodyPath, element.Id, value),
            EditDescriptionActionId when element is not null => new SetElementDescriptionCommand(bodyPath, element.Id, value),
            SetTechnologyActionId when element is not null => new SetElementTechnologyCommand(bodyPath, element.Id, value),
            RelabelActionId when relationship is not null => new SetRelationshipDescriptionCommand(bodyPath, relationship.Id, value),
            SetProtocolActionId when relationship is not null => new SetRelationshipTechnologyCommand(bodyPath, relationship.Id, value),
            _ => null,
        };

        if (command is null)
        {
            return ContextCommitResult.Failed($"Unknown action '{actionId}'.");
        }

        var result = await _historyStacks.Get(target.RootPath).ExecuteAsync(command, cancellationToken);
        return result.IsSuccess ? ContextCommitResult.Succeeded : ContextCommitResult.Failed(result.Error);
    }

    /// <summary>
    /// What the target names: an element, or a relationship, or neither. A relationship has no
    /// DSL identifier of its own, so it is looked up by the composite id the mapper puts on the
    /// wire for it.
    /// </summary>
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

    private static ValueTask<IReadOnlyList<ContextActionGroupDefinition>> Empty() =>
        ValueTask.FromResult<IReadOnlyList<ContextActionGroupDefinition>>([]);

    private static ValueTask<IReadOnlyList<ContextActionGroupDefinition>> Groups(params ContextActionGroupDefinition[] groups) =>
        ValueTask.FromResult<IReadOnlyList<ContextActionGroupDefinition>>(groups);

    private static ValueTask<ContextExecutionResult> Result(ContextExecutionResult result) =>
        ValueTask.FromResult(result);
}
