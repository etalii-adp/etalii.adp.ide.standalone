using EtAlii.Adp.Backend.Context;

namespace EtAlii.Adp.Backend.Hierarchy;

public sealed partial class HierarchyContextActionProvider
{
    public const string DeleteActionId = "hierarchy.delete";
    private static readonly ContextShortcutDefinition DeleteShortcut = new("Delete");

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

    private static string DeleteConfirmationMessage(string name, bool isFolder) => isFolder
        ? $"Delete the folder '{name}' and everything inside it from disk? This cannot be undone."
        : $"Delete '{name}' from disk? This cannot be undone.";
}
