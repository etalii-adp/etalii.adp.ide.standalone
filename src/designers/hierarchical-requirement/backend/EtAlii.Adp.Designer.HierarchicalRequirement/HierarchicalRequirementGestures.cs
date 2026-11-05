using EtAlii.Adp.Specification.Fbl.Planning;

namespace EtAlii.Adp.Designer.HierarchicalRequirement;

/// <summary>
/// The gesture table: which operation of <c>hierarchical-requirement.des</c> a gesture of the hierarchy surface names, and
/// what it changes (FR-026). The surface passes the operation's name through and knows none; an unknown name is refused.
/// </summary>
/// <remarks>Each pull request of spec 029 adds the operations of its user story; an operation of the specification not built yet is refused with the reason.</remarks>
internal static class HierarchicalRequirementGestures
{
    /// <summary>What a gesture planned against a file that changed since its model was sent is refused with (spec 029 R10).</summary>
    internal const string Drifted = "The file changed while you were editing, so this change was not written. The page now shows the file as it is.";

    /// <summary>The block of the registration that keeps what is collapsed, outside the history (hierarchical-requirement.fbl).</summary>
    private const string CollapsedBlock = "collapsed";

    private const string ToggleCollapsedOperation = "toggleCollapsed";

    /// <summary>
    /// The change of the registration <paramref name="gesture"/> asks for, against <paramref name="model"/>, the model it
    /// was made in as the session holds it now; or the sentence saying why it is refused.
    /// </summary>
    public static (ModelChange.Setting? Setting, string Refusal) Plan(HierarchySurfaceModel model, HierarchySurfaceGesture gesture)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(gesture);
        if (model.ReadOnlyReason.Length > 0) return (null, model.ReadOnlyReason);
        return gesture.Operation switch
        {
            ToggleCollapsedOperation => ToggleCollapsed(model, gesture.TargetId),
            _ when HierarchicalRequirementVocabulary.Operations.Contains(gesture.Operation) => (null, $"'{gesture.Operation}' cannot be done here yet."),
            _ => (null, $"'{gesture.Operation}' is not an operation of the Hierarchical Requirement Designer."),
        };
    }

    /// <summary>
    /// Collapses an expanded entry or expands a collapsed one, in the registration's <c>collapsed</c> block (FR-010). An
    /// entry without a usable number is collapsed for the session only, which is the client's (DESL section 5.3.3).
    /// </summary>
    private static (ModelChange.Setting?, string) ToggleCollapsed(HierarchySurfaceModel model, string targetId)
    {
        if (model.Nodes.FirstOrDefault(candidate => candidate.Id == targetId) is not { } node) return (null, Drifted);
        if (node.Id != node.Number) return (null, $"{node.Number} has no number of its own yet, so it is collapsed on this page only.");
        return node.Collapsed
            ? (new ModelChange.Setting(CollapsedBlock, node.Id, RemoveEntry: true), "")
            : (new ModelChange.Setting(CollapsedBlock, node.Id, "yes"), "");
    }
}
