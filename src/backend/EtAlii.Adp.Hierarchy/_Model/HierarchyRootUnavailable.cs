namespace EtAlii.Adp.Hierarchy;

/// <summary>The project's root folder itself became unreachable; nothing under it can be watched.</summary>
public sealed record HierarchyRootUnavailable(string Message) : HierarchyEntryChange;
