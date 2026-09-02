using EtAlii.Adp.Backend.Context;

namespace EtAlii.Adp.Backend.Hierarchy;

public sealed partial class HierarchyContextActionProvider
{
    public const string AddFolderActionId = "hierarchy.add-folder";

    // The creation itself lives in CreateFolderCommandHandler; what is left here is the
    // judgement of the name, which the dialog needs while the user is still typing and no
    // command has been raised yet. The rules are EntryNameRules' - shared with rename, so an
    // add and a rename can never disagree about what a usable name is.

    private static ContextValidationResult ValidateNewFolder(ContextTarget target, string value) =>
        EntryNameRules.Validate(value, target.ResolvedFullPath);
}
