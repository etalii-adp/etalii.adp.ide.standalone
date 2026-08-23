using EtAlii.Adp.Backend.Context;

namespace EtAlii.Adp.Backend.Hierarchy;

public sealed partial class HierarchyContextActionProvider
{
    public const string DeleteActionId = "hierarchy.delete";
    private static readonly ContextShortcutDefinition DeleteShortcut = new("Delete");

    // The removal itself lives in DeleteEntryCommandHandler; all that is left here is what
    // the user is asked before it happens.

    private static string DeleteConfirmationMessage(string name, bool isFolder) => isFolder
        ? $"Delete the folder '{name}' and everything inside it from disk? This cannot be undone."
        : $"Delete '{name}' from disk? This cannot be undone.";
}
