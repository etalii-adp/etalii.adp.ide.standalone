namespace EtAlii.Adp.Common;

/// <summary>
/// One recorded change on an <see cref="IHistoryStack"/>: the command that was executed,
/// paired with the command that reverses it.
/// </summary>
/// <remarks>
/// Both directions are stored so undo and redo are ordinary dispatches rather than a
/// second, parallel notion of "applying a change" - undo runs <see cref="Inverse"/>, redo
/// runs <see cref="Command"/> again.
/// </remarks>
public sealed record HistoryEntry(ICommand Command, ICommand Inverse);
