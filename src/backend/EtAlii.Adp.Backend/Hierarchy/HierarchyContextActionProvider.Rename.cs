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
}
