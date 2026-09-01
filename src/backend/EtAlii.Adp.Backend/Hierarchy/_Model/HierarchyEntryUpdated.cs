namespace EtAlii.Adp.Backend.Hierarchy;

/// <summary>
/// An entry's own detail changed: whether it has children, and - for a re-parent - which entry
/// is now its parent. <see cref="ParentChanged"/> is the tri-state's third leg: a null
/// <see cref="ParentId"/> WITH it set means "now at the root", which unset-means-unchanged
/// alone could not say (adp-file-nesting Requirement 10.3).
/// </summary>
public sealed record HierarchyEntryUpdated(ShortGuid EntryId, bool HasChildren, bool ParentChanged = false, ShortGuid? ParentId = null) : HierarchyEntryChange;
