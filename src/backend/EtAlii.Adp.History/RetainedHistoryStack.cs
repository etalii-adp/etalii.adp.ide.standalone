namespace EtAlii.Adp.History;

internal sealed class RetainedHistoryStack(ProjectRoot root, IHistoryStack stack)
{
    public ProjectRoot Root { get; } = root;

    public IHistoryStack Stack { get; } = stack;

    public EventHandler? Handler { get; set; }
}
