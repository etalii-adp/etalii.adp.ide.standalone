using EtAlii.Adp.Backend.Context;
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Backend.Hierarchy;

/// <summary>
/// The core provider for the explorer: Rename and Delete, plus the filesystem work behind
/// both. It is registered like any other <see cref="IContextActionProvider"/>, so a later
/// module contributing its own file-type-specific actions sits beside it rather than
/// inside it.
/// </summary>
public sealed class HierarchyContextActionProvider : IContextActionProvider
{
    public const string RenameActionId = "hierarchy.rename";
    public const string DeleteActionId = "hierarchy.delete";

    private static readonly ContextShortcutDefinition RenameShortcut = new("F2");
    private static readonly ContextShortcutDefinition DeleteShortcut = new("Delete");

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
        if (actionId != RenameActionId)
        {
            // Delete takes no value, so there is nothing to judge.
            return ValueTask.FromResult(ContextValidationResult.Accepted);
        }

        return ValueTask.FromResult(ValidateRename(target, value));
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

    private static ContextCommitResult Rename(ContextTarget target, string newName)
    {
        var destination = IoPath.Combine(ParentFolderOf(target)!, newName);
        try
        {
            // A single move, never a copy-then-delete: a folder keeps its entire nested
            // contents untouched, and the operation cannot leave a half-renamed state.
            if (target.IsContainer)
            {
                Directory.Move(target.ResolvedFullPath, destination);
            }
            else
            {
                File.Move(target.ResolvedFullPath, destination);
            }

            return ContextCommitResult.Succeeded;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ContextCommitResult.Failed($"Could not rename this item: {ex.Message}");
        }
    }

    private static ContextCommitResult Delete(ContextTarget target)
    {
        try
        {
            if (target.IsContainer)
            {
                Directory.Delete(target.ResolvedFullPath, recursive: true);
            }
            else
            {
                File.Delete(target.ResolvedFullPath);
            }

            return ContextCommitResult.Succeeded;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A recursive delete can fail partway through, so say that the delete did not
            // complete rather than that nothing happened; whatever did get removed arrives
            // as ordinary changes from the watcher, leaving the tree showing real state.
            var remains = target.IsContainer && Directory.Exists(target.ResolvedFullPath);
            var suffix = remains ? " Some of its contents may already have been removed." : "";
            return ContextCommitResult.Failed($"Could not fully delete this item: {ex.Message}{suffix}");
        }
    }

    private static ContextValidationResult ValidateRename(ContextTarget target, string value)
    {
        var newName = value.Trim();
        if (newName.Length == 0)
        {
            return ContextValidationResult.Rejected("Enter a name.");
        }

        if (ParentFolderOf(target) is not { } parentFolder)
        {
            return ContextValidationResult.Rejected("This item has no parent folder to act within.");
        }

        // A rename changes the name, not the location - so a value that is anything other
        // than a bare name is refused outright. Rejecting separators, relative segments and
        // rooted paths here is also what keeps a "rename" from reaching outside the root:
        // the destination is checked below to still sit in this very parent folder.
        if (newName is "." or ".." ||
            newName.IndexOfAny(new[] { IoPath.DirectorySeparatorChar, IoPath.AltDirectorySeparatorChar, ':' }) >= 0 ||
            newName.IndexOfAny(IoPath.GetInvalidFileNameChars()) >= 0)
        {
            return ContextValidationResult.Rejected("A name cannot contain a path or any of \\ / : * ? \" < > |");
        }

        var currentName = IoPath.GetFileName(target.ResolvedFullPath);
        if (string.Equals(newName, currentName, StringComparison.Ordinal))
        {
            return ContextValidationResult.Rejected("Enter a name that differs from the current one.");
        }

        string destination;
        try
        {
            destination = IoPath.GetFullPath(IoPath.Combine(parentFolder, newName));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return ContextValidationResult.Rejected("That name cannot be used on this system.");
        }

        var destinationParent = IoPath.GetDirectoryName(destination);
        if (destinationParent is null || !string.Equals(destinationParent, parentFolder, StringComparison.OrdinalIgnoreCase))
        {
            return ContextValidationResult.Rejected("A rename can only change the name, not move the item.");
        }

        // On a case-insensitive filesystem, renaming only the casing has the item colliding
        // with itself; that is a legitimate rename, so let the move handle it.
        var isSameEntry = string.Equals(destination, target.ResolvedFullPath, StringComparison.OrdinalIgnoreCase);
        if (!isSameEntry && (File.Exists(destination) || Directory.Exists(destination)))
        {
            return ContextValidationResult.Rejected($"An item named '{newName}' already exists in this folder.");
        }

        return ContextValidationResult.Accepted;
    }

    private static string DeleteConfirmationMessage(string name, bool isFolder) => isFolder
        ? $"Delete the folder '{name}' and everything inside it from disk? This cannot be undone."
        : $"Delete '{name}' from disk? This cannot be undone.";

    private static string? ParentFolderOf(ContextTarget target) => IoPath.GetDirectoryName(target.ResolvedFullPath);

    private static bool Exists(ContextTarget target) =>
        target.IsContainer ? Directory.Exists(target.ResolvedFullPath) : File.Exists(target.ResolvedFullPath);
}
