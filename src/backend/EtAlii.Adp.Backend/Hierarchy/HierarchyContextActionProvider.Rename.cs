using EtAlii.Adp.Backend.Context;
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Backend.Hierarchy;

public sealed partial class HierarchyContextActionProvider
{
    public const string RenameActionId = "hierarchy.rename";
    private static readonly ContextShortcutDefinition RenameShortcut = new("F2");

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

    private static ContextValidationResult ValidateRename(ContextTarget target, string value)
    {
        if (ParentFolderOf(target) is not { } parentFolder)
        {
            return ContextValidationResult.Rejected("This item has no parent folder to act within.");
        }

        // Every other rule lives in EntryNameRules, shared with creating a new entry, so a
        // rename and an add can never disagree about what a usable name is.
        return EntryNameRules.Validate(value, parentFolder, IoPath.GetFileName(target.ResolvedFullPath));
    }
}