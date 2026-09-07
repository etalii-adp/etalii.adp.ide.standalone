using EtAlii.Adp.Common;

namespace EtAlii.Adp.Hierarchy;

public sealed partial class HierarchyContextActionProvider
{
    public const string DeleteActionId = "hierarchy.delete";
    private static readonly ContextShortcutDefinition DeleteShortcut = new("Delete");

    // The removal itself lives in DeleteEntryCommandHandler; all that is left here is what
    // the user is asked before it happens - including how many diagram registrations a
    // subject's delete will take with it, said BEFORE it runs (adp-file-nesting
    // Requirement 6.1).

    /// <summary>How many registrations a delete of this target would cascade to (0 for folders and registrations).</summary>
    private int CascadeCountOf(ContextTarget target)
    {
        if (target.IsContainer || DiagramFilePair.IsRegistrationFile(target.ResolvedFullPath))
        {
            return 0;
        }

        var parent = Path.GetDirectoryName(target.ResolvedFullPath);
        return parent is null ? 0 : DiagramRegistrationSet.Over(parent, target.ResolvedFullPath, _catalog).Count();
    }

    public static string DeleteConfirmationMessage(string name, bool isFolder, int registrationCount = 0)
    {
        if (isFolder)
        {
            return $"Delete the folder '{name}' and everything inside it from disk? This cannot be undone.";
        }

        return registrationCount switch
        {
            0 => $"Delete '{name}' from disk? This cannot be undone.",
            1 => $"Delete '{name}' and the 1 diagram registered on it from disk? This cannot be undone.",
            _ => $"Delete '{name}' and the {registrationCount} diagrams registered on it from disk? This cannot be undone.",
        };
    }
}
