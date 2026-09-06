using EtAlii.Adp.Common;
namespace EtAlii.Adp.Backend;

internal sealed class RetainedHistoryStack(string rootPath, IHistoryStack stack)
{
    public string RootPath { get; } = rootPath;

    public IHistoryStack Stack { get; } = stack;

    public object Gate { get; } = new();

    public int RefCount { get; set; }

    public Timer? EvictionTimer { get; set; }

    public EventHandler? Handler { get; set; }
}
