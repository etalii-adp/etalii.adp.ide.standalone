namespace EtAlii.Adp.Hierarchy;

/// <summary>
/// Raised by <see cref="HierarchyModel"/> for a connection's own <c>WatchHierarchy</c>
/// stream; <c>HierarchyServiceImpl</c> maps each variant to its wire message.
/// </summary>
/// <remarks>A closed set: the five records declared beside this one.</remarks>
public abstract record HierarchyEntryChange;
