using EtAlii.Adp.Context;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Documents.Wire;
using EtAlii.Adp.History;
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Hierarchy;

/// <summary>
/// The core provider for the explorer: Rename and Delete, plus the filesystem work behind
/// both. It is registered like any other <see cref="IContextActionProvider"/>, so a later
/// module contributing its own file-type-specific actions sits beside it rather than
/// inside it.
/// </summary>
public sealed partial class HierarchyContextActionProvider : IContextActionProvider
{
    private const string RootUntouchable = "The project folder itself cannot be renamed or deleted here.";

    private readonly IHistoryStackStore _historyStacks;
    private readonly IDiagramDefinitionCatalog _catalog;

    /// <param name="historyStacks">
    /// Where both actions send their work, scoped to the target's project. Neither touches the
    /// filesystem itself: a rename and a delete are state changes, so they go out as commands
    /// and the project's history is what records them.
    /// </param>
    /// <param name="catalog">
    /// How the delete confirmation knows a file is a subject: the registrations over it are
    /// counted so the dialog can say how many go with it, before anything runs
    /// (adp-file-nesting Requirement 6.1).
    /// </param>
    public HierarchyContextActionProvider(IHistoryStackStore historyStacks, IDiagramDefinitionCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(historyStacks);
        ArgumentNullException.ThrowIfNull(catalog);
        _historyStacks = historyStacks;
        _catalog = catalog;
    }

    public ContextScope Scope => ContextScope.Hierarchy;

    public ValueTask<IReadOnlyList<ContextActionGroupDefinition>> DiscoverAsync(ContextTarget target, CancellationToken cancellationToken)
    {
        if (!Exists(target))
        {
            // Gone between being listed and being asked about: reporting actions here would
            // only offer operations that are already certain to fail (Requirement 2.7).
            return ValueTask.FromResult<IReadOnlyList<ContextActionGroupDefinition>>(Array.Empty<ContextActionGroupDefinition>());
        }

        // Neither action is ever omitted - an entry that can't currently be touched reports
        // both as unavailable with the reason, so the menu can show them greyed and explain.
        // The project root is the one folder that must never be renamed or deleted from
        // inside the project it *is*; a parent-folder check would not catch it (only a drive
        // root has no parent), so it is recognised outright.
        var unavailableReason = HierarchyTargets.IsRoot(target)
            ? RootUntouchable
            : ParentFolderOf(target) is null
                ? "This item has no parent folder to act within."
                : "";
        var available = unavailableReason.Length == 0;

        var group = new ContextActionGroupDefinition(new[]
        {
            new ContextActionDefinition(
                RenameActionId, "Rename…", "mdi-pencil-outline", RenameShortcut, available, unavailableReason),
            new ContextActionDefinition(
                DeleteActionId, "Delete", "mdi-trash-can-outline", DeleteShortcut, available, unavailableReason),
        });

        if (!target.IsContainer)
        {
            return ValueTask.FromResult<IReadOnlyList<ContextActionGroupDefinition>>(new[] { group });
        }

        // A folder also offers to grow a subfolder - the project root included, which is the
        // one place rename and delete rightly refuse but an add belongs most.
        var additions = new ContextActionGroupDefinition(new[]
        {
            new ContextActionDefinition(
                AddFolderActionId, "New folder…", "mdi-folder-plus-outline", Shortcut: null, Available: true, UnavailableReason: ""),
        });

        return ValueTask.FromResult<IReadOnlyList<ContextActionGroupDefinition>>(new[] { group, additions });
    }

    public ValueTask<ContextExecutionResult> ExecuteAsync(ContextTarget target, string actionId, CancellationToken cancellationToken)
    {
        if (!Exists(target))
        {
            return ValueTask.FromResult<ContextExecutionResult>(
                new ContextExecutionFailed("This item no longer exists."));
        }

        // Adding INTO a folder is fine anywhere, the root included; only acting ON the root
        // itself is refused.
        if (HierarchyTargets.IsRoot(target) && actionId != AddFolderActionId)
        {
            return ValueTask.FromResult<ContextExecutionResult>(new ContextExecutionFailed(RootUntouchable));
        }

        var name = IoPath.GetFileName(target.ResolvedFullPath);

        return ValueTask.FromResult<ContextExecutionResult>(actionId switch
        {
            AddFolderActionId when target.IsContainer => new ContextExecutionRequiresInput(
                new ContextInputRequest(
                    Title: "New folder",
                    Icon: "mdi-folder-plus-outline",
                    FieldLabel: "Name",
                    InitialValue: "",
                    ConfirmLabel: "Create")),

            RenameActionId => new ContextExecutionRequiresInput(
                new ContextInputRequest(
                    Title: target.IsContainer ? "Rename folder" : "Rename file",
                    Icon: "mdi-pencil-outline",
                    FieldLabel: "New name",
                    InitialValue: name,
                    ConfirmLabel: "Rename")),

            DeleteActionId => new ContextExecutionRequiresConfirmation(
                new ContextConfirmationRequest(
                    Title: target.IsContainer ? "Delete folder?" : "Delete file?",
                    Icon: "mdi-trash-can-outline",
                    Message: DeleteConfirmationMessage(name, target.IsContainer, CascadeCountOf(target)),
                    ConfirmLabel: "Delete",
                    Danger: true)),

            _ => new ContextExecutionFailed($"Unknown action '{actionId}'."),
        });
    }

    /// <summary>
    /// The single validation implementation: it answers the dialog's live verdict and the
    /// re-check performed just before committing, so a client that skipped the former still
    /// cannot get past the latter.
    /// </summary>
    public ValueTask<ContextValidationResult> ValidateAsync(ContextTarget target, string actionId, string value, CancellationToken cancellationToken)
    {
        return ValueTask.FromResult(actionId switch
        {
            RenameActionId => ValidateRename(target, value),
            AddFolderActionId => ValidateNewFolder(target, value),
            _ => ContextValidationResult.Accepted, // Delete takes no value, so there is nothing to judge.
        });
    }

    public async ValueTask<ContextCommitResult> CommitAsync(ContextTarget target, string actionId, string value, string text, CancellationToken cancellationToken)
    {
        // Re-checked here, not only in ExecuteAsync: a client that skipped the prompt step
        // must still be unable to rename or delete the project folder. Adding into it stays
        // allowed - the add acts within the folder, never on it.
        if (HierarchyTargets.IsRoot(target) && actionId != AddFolderActionId)
        {
            return ContextCommitResult.Failed(RootUntouchable);
        }

        var validation = await ValidateAsync(target, actionId, value, cancellationToken);
        if (!validation.Valid)
        {
            return ContextCommitResult.Failed(validation.Reason);
        }

        return actionId switch
        {
            RenameActionId => await DispatchAsync(target, new RenameEntryCommand(target.ResolvedFullPath, value), cancellationToken),
            DeleteActionId => await DispatchAsync(target, new DeleteEntryCommand(target.ResolvedFullPath), cancellationToken),
            AddFolderActionId when target.IsContainer => await CreateFolderAsync(target, value.Trim(), cancellationToken),
            _ => ContextCommitResult.Failed($"Unknown action '{actionId}'."),
        };
    }

    /// <summary>
    /// Runs one command through the history and turns its result into the answer the dialog
    /// expects. The handler's own message is passed straight through: it knows what actually
    /// stopped it, which is more than this provider can say from here.
    /// </summary>
    private async ValueTask<ContextCommitResult> DispatchAsync(ContextTarget target, ICommand command, CancellationToken cancellationToken)
    {
        var result = await _historyStacks.Get(target.RootPath).ExecuteAsync(command, cancellationToken);
        return result.IsSuccess ? ContextCommitResult.Succeeded : ContextCommitResult.Failed(result.Error);
    }

    /// <summary>
    /// Creates the subfolder and answers with its location, so the explorer can reveal what
    /// just appeared instead of leaving the user to hunt for it.
    /// </summary>
    private async ValueTask<ContextCommitResult> CreateFolderAsync(ContextTarget target, string name, CancellationToken cancellationToken)
    {
        var fullPath = IoPath.Combine(target.ResolvedFullPath, name);
        var result = await _historyStacks.Get(target.RootPath).ExecuteAsync(new CreateFolderCommand(fullPath), cancellationToken);
        return result.IsSuccess ? ContextCommitResult.Created(fullPath) : ContextCommitResult.Failed(result.Error);
    }

    private static string? ParentFolderOf(ContextTarget target) => IoPath.GetDirectoryName(target.ResolvedFullPath);

    private static bool Exists(ContextTarget target) => HierarchyTargets.Exists(target);
}
