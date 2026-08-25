namespace EtAlii.Adp.Backend.Hierarchy;

/// <summary>An entry appeared under a folder this connection is watching.</summary>
public sealed record HierarchyEntryCreated(EntryNode Entry) : HierarchyEntryChange;
