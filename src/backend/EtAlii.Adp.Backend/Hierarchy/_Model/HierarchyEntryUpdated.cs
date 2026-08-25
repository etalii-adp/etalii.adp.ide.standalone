namespace EtAlii.Adp.Backend.Hierarchy;

/// <summary>An entry's own detail changed - today, whether a folder still has children.</summary>
public sealed record HierarchyEntryUpdated(ShortGuid EntryId, bool HasChildren) : HierarchyEntryChange;
