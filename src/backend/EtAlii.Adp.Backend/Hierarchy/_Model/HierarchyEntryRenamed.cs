namespace EtAlii.Adp.Backend.Hierarchy;

/// <summary>An entry kept its id and took a new name - what makes a rename survivable for a selection.</summary>
public sealed record HierarchyEntryRenamed(ShortGuid EntryId, string NewName) : HierarchyEntryChange;
