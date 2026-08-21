namespace EtAlii.Adp.Backend.Hierarchy;

/// <summary>
/// Raised by <see cref="HierarchyModel"/> for a connection's own <c>WatchHierarchy</c>
/// stream; <c>HierarchyServiceImpl</c> maps each variant to its wire message.
/// </summary>
public abstract record HierarchyEntryChange
{
    public sealed record Created(EntryNode Entry) : HierarchyEntryChange;

    public sealed record Removed(ShortGuid EntryId) : HierarchyEntryChange;

    public sealed record Renamed(ShortGuid EntryId, string NewName) : HierarchyEntryChange;

    public sealed record Updated(ShortGuid EntryId, bool HasChildren) : HierarchyEntryChange;

    public sealed record RootUnavailable(string Message) : HierarchyEntryChange;
}
