using EtAlii.Adp.Common;
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Hierarchy;

public sealed partial class HierarchyContextActionProvider
{
    public const string RenameActionId = "hierarchy.rename";
    private static readonly ContextShortcutDefinition RenameShortcut = new("F2");

    // The move itself lives in RenameEntryCommandHandler; what is left here is the judgement
    // of the name, which the dialog needs while the user is still typing and no command has
    // been raised yet.

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