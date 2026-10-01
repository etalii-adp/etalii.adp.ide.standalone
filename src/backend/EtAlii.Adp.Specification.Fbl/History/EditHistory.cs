namespace EtAlii.Adp.Specification.Fbl.History;

using System.Security.Cryptography;

/// <summary>
/// The history of one body (FBL §7.1): each edit with the splices that undo it and the digests of
/// the body before and after it. A snapshot edit keeps the whole body before it as well; its undo
/// gives the same bytes as the inverse splices.
/// </summary>
internal sealed class EditHistory
{
    private readonly Stack<HistoryEntry> _undo = new();
    private readonly Stack<HistoryEntry> _redo = new();

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    public void Record(byte[] before, byte[] after, Edit edit)
    {
        _undo.Push(new HistoryEntry(
            edit,
            Edit.Inverse(before, edit.Splices),
            SHA256.HashData(before),
            SHA256.HashData(after),
            edit.Snapshot ? before : null));
        _redo.Clear();
    }

    public HistoryEntry PeekUndo() => _undo.Peek();

    public HistoryEntry PeekRedo() => _redo.Peek();

    public void Undone() => _redo.Push(_undo.Pop());

    public void Redone() => _undo.Push(_redo.Pop());

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
    }
}

internal sealed record HistoryEntry(Edit Edit, IReadOnlyList<Splice> Inverse, byte[] BeforeDigest, byte[] AfterDigest, byte[]? Snapshot);
