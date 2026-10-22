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

    /// <summary>
    /// Carries every entry over an edit that is no step of the history (FBL 0.7 §7.1): <paramref name="through"/>,
    /// already applied, which made the body <paramref name="current"/>. Each entry's splices are moved by the bytes the
    /// edit added or removed before them, and its digests are those of the bodies the entries now lead through. When the
    /// edit overlaps an entry's splices, the history is cleared: that entry cannot be undone without undoing the edit.
    /// </summary>
    public void CarryOver(byte[] current, IReadOnlyList<Splice> through)
    {
        var undo = new List<HistoryEntry>();
        var state = current;
        var outside = through;
        foreach (var entry in _undo)
        {
            if (Moved(entry.Inverse, outside) is not { } inverse)
            {
                Clear();
                return;
            }
            var before = Edit.Apply(state, inverse);
            var edit = entry.Edit with { Splices = Edit.Inverse(state, inverse), Snapshot = false };
            undo.Add(new HistoryEntry(edit, inverse, SHA256.HashData(before), SHA256.HashData(state), null));
            if (Moved(outside, entry.Inverse) is not { } earlier)
            {
                Clear();
                return;
            }
            outside = earlier;
            state = before;
        }
        var redo = new List<HistoryEntry>();
        state = current;
        outside = through;
        foreach (var entry in _redo)
        {
            if (Moved(entry.Edit.Splices, outside) is not { } splices)
            {
                Clear();
                return;
            }
            var after = Edit.Apply(state, splices);
            redo.Add(new HistoryEntry(entry.Edit with { Splices = splices, Snapshot = false }, Edit.Inverse(state, splices), SHA256.HashData(state), SHA256.HashData(after), null));
            if (Moved(outside, entry.Edit.Splices) is not { } later)
            {
                Clear();
                return;
            }
            outside = later;
            state = after;
        }
        _undo.Clear();
        for (var i = undo.Count - 1; i >= 0; i--) _undo.Push(undo[i]);
        _redo.Clear();
        for (var i = redo.Count - 1; i >= 0; i--) _redo.Push(redo[i]);
    }

    /// <summary>
    /// <paramref name="splices"/> with their offsets moved over <paramref name="through"/>, both in the offsets of one body
    /// (an edit outside the history is carried past an entry by that entry's own splices, not by its moved ones):
    /// by the bytes every splice of <paramref name="through"/> that ends before one (or inserts where it starts) adds or
    /// removes. Null when the two overlap.
    /// </summary>
    private static List<Splice>? Moved(IReadOnlyList<Splice> splices, IReadOnlyList<Splice> through)
    {
        var moved = new List<Splice>(splices.Count);
        foreach (var splice in splices)
        {
            var shift = 0;
            foreach (var other in through)
            {
                if (other.Start < splice.End && splice.Start < other.End) return null;
                if (other.End <= splice.Start) shift += System.Text.Encoding.UTF8.GetByteCount(other.Text) - (other.End - other.Start);
            }
            moved.Add(splice with { Start = splice.Start + shift, End = splice.End + shift });
        }
        return moved;
    }

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
    }
}

internal sealed record HistoryEntry(Edit Edit, IReadOnlyList<Splice> Inverse, byte[] BeforeDigest, byte[] AfterDigest, byte[]? Snapshot);
