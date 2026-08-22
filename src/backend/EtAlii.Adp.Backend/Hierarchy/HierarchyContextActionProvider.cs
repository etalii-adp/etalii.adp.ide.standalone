using EtAlii.Adp.Backend.Context;
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Backend.Hierarchy;

/// <summary>
/// The core provider for the explorer: Rename and Delete, plus the filesystem work behind
/// both. It is registered like any other <see cref="IContextActionProvider"/>, so a later
/// module contributing its own file-type-specific actions sits beside it rather than
/// inside it.
/// </summary>
public sealed partial class HierarchyContextActionProvider : IContextActionProvider
{
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
        var unavailableReason = ParentFolderOf(target) is null
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

        return ValueTask.FromResult<IReadOnlyList<ContextActionGroupDefinition>>(new[] { group });
    }

    public ValueTask<ContextExecutionResult> ExecuteAsync(ContextTarget target, string actionId, CancellationToken cancellationToken)
    {
        if (!Exists(target))
        {
            return ValueTask.FromResult<ContextExecutionResult>(
                new ContextExecutionResult.Failed("This item no longer exists."));
        }

        var name = IoPath.GetFileName(target.ResolvedFullPath);

        return ValueTask.FromResult<ContextExecutionResult>(actionId switch
        {
            RenameActionId => new ContextExecutionResult.RequiresInput(
                new ContextInputRequest(
                    Title: target.IsContainer ? "Rename folder" : "Rename file",
                    Icon: "mdi-pencil-outline",
                    FieldLabel: "New name",
                    InitialValue: name,
                    ConfirmLabel: "Rename")),

            DeleteActionId => new ContextExecutionResult.RequiresConfirmation(
                new ContextConfirmationRequest(
                    Title: target.IsContainer ? "Delete folder?" : "Delete file?",
                    Icon: "mdi-trash-can-outline",
                    Message: DeleteConfirmationMessage(name, target.IsContainer),
                    ConfirmLabel: "Delete",
                    Danger: true)),

            _ => new ContextExecutionResult.Failed($"Unknown action '{actionId}'."),
        });
    }

    /// <summary>
    /// The single validation implementation: it answers the dialog's live verdict and the
    /// re-check performed just before committing, so a client that skipped the former still
    /// cannot get past the latter.
    /// </summary>
    public ValueTask<ContextValidationResult> ValidateAsync(ContextTarget target, string actionId, string value, CancellationToken cancellationToken)
    {
        return ValueTask.FromResult(actionId != RenameActionId
            ? ContextValidationResult.Accepted // Delete takes no value, so there is nothing to judge.
            : ValidateRename(target, value));
    }

    public async ValueTask<ContextCommitResult> CommitAsync(ContextTarget target, string actionId, string value, CancellationToken cancellationToken)
    {
        var validation = await ValidateAsync(target, actionId, value, cancellationToken);
        if (!validation.Valid)
        {
            return ContextCommitResult.Failed(validation.Reason);
        }

        return actionId switch
        {
            RenameActionId => Rename(target, value),
            DeleteActionId => Delete(target),
            _ => ContextCommitResult.Failed($"Unknown action '{actionId}'."),
        };
    }

    private static string? ParentFolderOf(ContextTarget target) => IoPath.GetDirectoryName(target.ResolvedFullPath);

    private static bool Exists(ContextTarget target) =>
        target.IsContainer ? Directory.Exists(target.ResolvedFullPath) : File.Exists(target.ResolvedFullPath);
}
