namespace EtAlii.Adp.Hierarchy;

/// <summary>
/// An entry's own detail changed: whether it has children, and - for a re-parent - which entry
/// is now its parent. <see cref="ParentChanged"/> is the tri-state's third leg: a null
/// <see cref="ParentId"/> WITH it set means "now at the root", which unset-means-unchanged
/// alone could not say (adp-file-nesting Requirement 10.3).
/// <see cref="DiagramState"/> always carries the entry's full current state, never a delta -
/// like <see cref="HasChildren"/>, because an unset enum is indistinguishable from a genuine
/// neutral (small-refinements Requirement 3.6).
/// </summary>
public sealed record HierarchyEntryUpdated(ShortGuid EntryId, bool HasChildren, bool ParentChanged = false, ShortGuid? ParentId = null, EntryDiagramState DiagramState = EntryDiagramState.Unspecified) : HierarchyEntryChange;
