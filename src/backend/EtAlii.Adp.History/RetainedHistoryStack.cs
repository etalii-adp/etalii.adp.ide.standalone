namespace EtAlii.Adp.History;

internal sealed class RetainedHistoryStack(string rootPath, IHistoryStack stack)
{
    public string RootPath { get; } = rootPath;

    public IHistoryStack Stack { get; } = stack;

    public EventHandler? Handler { get; set; }
}
