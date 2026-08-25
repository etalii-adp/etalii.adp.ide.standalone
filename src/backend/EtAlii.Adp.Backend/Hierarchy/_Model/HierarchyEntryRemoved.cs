namespace EtAlii.Adp.Backend.Hierarchy;

/// <summary>An entry this connection knows about is gone.</summary>
public sealed record HierarchyEntryRemoved(ShortGuid EntryId) : HierarchyEntryChange;
