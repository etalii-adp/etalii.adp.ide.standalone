namespace EtAlii.Adp.Hierarchy;

internal sealed class HierarchyModelEntry
{
    public required HierarchyModel Model { get; init; }
    /// <summary>The watch call's subscription to the project's file events, once one claims the entry.</summary>
    public IDisposable? Events { get; set; }
}
