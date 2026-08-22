namespace EtAlii.Adp.Backend;

/// <summary>
/// Executes commands and remembers the undoable ones, so they can be walked back and
/// forward again.
/// </summary>
/// <remarks>
/// Callers that want a change to be undoable go through <see cref="ExecuteAsync"/> rather
/// than <see cref="ICommandDispatcher"/> directly; dispatching straight past the stack is
/// the way to run something deliberately outside the history.
/// </remarks>
public interface IHistoryStack
{
    bool CanUndo { get; }

    bool CanRedo { get; }

    /// <summary>Recorded changes currently available to undo.</summary>
    int UndoCount { get; }

    /// <summary>Undone changes currently available to redo.</summary>
    int RedoCount { get; }

    /// <summary>
    /// Runs <paramref name="command"/> and, if it succeeds and reports an inverse, records it -
    /// which also discards any redo entries, since the future they belonged to no longer exists.
    /// </summary>
    Task<CommandResult> ExecuteAsync(ICommand command, CancellationToken cancellationToken = default);

    /// <summary>Reverses the most recent recorded change, moving it to the redo side.</summary>
    Task<CommandResult> UndoAsync(CancellationToken cancellationToken = default);

    /// <summary>Re-applies the most recently undone change, moving it back to the undo side.</summary>
    Task<CommandResult> RedoAsync(CancellationToken cancellationToken = default);

    /// <summary>Forgets everything on both sides, without touching the state the commands changed.</summary>
    void Clear();
}
