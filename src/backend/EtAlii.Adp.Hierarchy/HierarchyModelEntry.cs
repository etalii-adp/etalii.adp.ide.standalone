namespace EtAlii.Adp.Hierarchy;

internal sealed class HierarchyModelEntry
{
    public required HierarchyModel Model { get; init; }
    public RootFolderWatcher? Watcher { get; set; }
    public Timer? IdleTimer { get; set; }
}
