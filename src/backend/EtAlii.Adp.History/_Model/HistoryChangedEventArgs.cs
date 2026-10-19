namespace EtAlii.Adp.History;

/// <summary>Which project's history changed, so the broadcaster knows whose connections to update.</summary>
public sealed class HistoryChangedEventArgs(ProjectRoot root) : EventArgs
{
    public ProjectRoot Root { get; } = root;
}
