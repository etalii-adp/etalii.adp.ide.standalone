using EtAlii.Adp.History;

namespace EtAlii.Adp.Sessions;

/// <summary>
/// The project's history as a part of a <see cref="ProjectSession"/>: retained while the connection
/// has the project open and released with it, so the stack outlives its last connection only by the
/// store's grace (diagram-undo-redo Requirement 4.2).
/// </summary>
public sealed class HistoryRetention(IHistoryStackStore stacks) : IProjectSessionPart
{
    public IDisposable Acquire(ProjectRoot root)
    {
        var rootPath = root.FullPath;
        stacks.Retain(rootPath);
        return Disposable.From(() => stacks.Release(rootPath));
    }
}
