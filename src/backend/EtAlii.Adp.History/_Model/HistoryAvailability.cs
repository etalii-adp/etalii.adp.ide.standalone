namespace EtAlii.Adp.History;

/// <summary>
/// Whether undo and redo are possible, and how deep each side is, read together under one
/// acquisition of the stack's lock - so a caller describing both actions can never take Undo
/// from one moment and Redo from another (diagram-undo-redo Requirement 5.2).
/// </summary>
public sealed record HistoryAvailability(bool CanUndo, bool CanRedo, int UndoCount, int RedoCount)
{
    /// <summary>An empty history: nothing to undo, nothing to redo.</summary>
    public static HistoryAvailability Empty { get; } = new(false, false, 0, 0);
}
