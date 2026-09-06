namespace EtAlii.Adp.Common;

/// <summary>
/// One <see cref="IHistoryStack"/> per project, keyed by its resolved root path rather than a
/// project id, so two users who opened the same folder as two differently-identified projects
/// share one history - they are editing the same files (diagram-undo-redo Requirement 1). A
/// stack is created on first ask and released when the project's last connection goes.
/// </summary>
public interface IHistoryStackStore
{
    /// <summary>The stack for <paramref name="rootPath"/>, created on first ask; the same instance thereafter. Never null.</summary>
    IHistoryStack Get(string rootPath);

    /// <summary>A connection begins watching the project: keep its stack alive while it is here.</summary>
    void Retain(string rootPath);

    /// <summary>A connection stops watching: on the last release the stack is dropped, behind a short grace so a reconnect keeps its history.</summary>
    void Release(string rootPath);

    /// <summary>Raised when any held stack's history changes, carrying which project's it was.</summary>
    event EventHandler<HistoryChangedEventArgs>? Changed;
}
