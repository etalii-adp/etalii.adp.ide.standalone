namespace EtAlii.Adp.Backend;

/// <summary>Which project's history changed, so the broadcaster knows whose connections to update.</summary>
public sealed class HistoryChangedEventArgs(string rootPath) : EventArgs
{
    public string RootPath { get; } = rootPath;
}
