using System.Security.Cryptography;
using EtAlii.Adp.Specification.Fbl.History;

namespace EtAlii.Adp.Specification.Fbl;

/// <summary>
/// A file written only by splices, with the one history of its edits (FBL §7.1): a body, or a
/// registration. Applying an edit applies its splices and reads the file again. Undo and redo
/// check for drift first and refuse with FBL §7.2's sentence, writing nothing (FBL §7.2).
/// </summary>
public abstract class SplicedFile
{
    /// <summary>FBL §7.2's sentence for an undo refused because the file changed underneath it.</summary>
    public const string DriftUndo = "The file has changed since this edit, so it cannot be undone.";

    public const string DriftRedo = "The file has changed since this edit was undone, so it cannot be redone.";

    /// <summary>FBL 0.7 §7.2's sentence for an edit refused because the file changed after it was read, before the edit was applied.</summary>
    public const string DriftEdit = "The file has changed since this edit was made, so it was not written.";

    private readonly EditHistory _history = new();

    /// <summary>The bytes before the open group, or null when no group is open.</summary>
    private byte[]? _groupBefore;

    protected SplicedFile(byte[] bytes)
    {
        Bytes = bytes;
    }

    /// <summary>The file's bytes after every applied edit.</summary>
    public byte[] Bytes { get; private set; }

    public bool CanUndo => _history.CanUndo;

    /// <summary>Applies an edit planned against the current bytes, records it, and reads the file again.</summary>
    public void Apply(SpliceEdit edit)
    {
        ArgumentNullException.ThrowIfNull(edit);
        if (edit.Splices.Count == 0) return;
        var before = Bytes;
        var after = SpliceEdit.Apply(before, edit.Splices);
        if (_groupBefore is null) _history.Record(before, after, edit);
        Replace(after);
    }

    /// <summary>
    /// Applies an edit that is no step of the history (FBL 0.7 §7.1, a block declared <c>history: false</c>), and reads the
    /// file again. The history is kept: every entry is carried over the edit, so an undo or a redo after it passes it by
    /// and writes its own splices at the offsets the edit moved. An entry whose splices the edit overlaps cannot be
    /// carried, and the history is cleared instead.
    /// </summary>
    protected void ApplyOutsideHistory(SpliceEdit edit)
    {
        ArgumentNullException.ThrowIfNull(edit);
        if (edit.Splices.Count == 0) return;
        if (_groupBefore is not null) throw new InvalidOperationException("An edit outside the history cannot be applied inside a group.");
        var after = SpliceEdit.Apply(Bytes, edit.Splices);
        _history.CarryOver(after, edit.Splices);
        Replace(after);
    }

    /// <summary>
    /// Applies the edits <paramref name="edits"/> makes as one history entry, so that the changes of one gesture are
    /// one undo step: inside the group each edit is applied and the file read again as usual, and when it closes the
    /// file before it and after it are recorded as one edit, the smallest splice between them. A group that changes
    /// nothing records nothing; one whose action throws records what it applied before it threw.
    /// </summary>
    /// <exception cref="InvalidOperationException">A group is already open on this file.</exception>
    public void Group(Action edits)
    {
        ArgumentNullException.ThrowIfNull(edits);
        if (_groupBefore is not null) throw new InvalidOperationException("A group is already open on this file.");
        _groupBefore = Bytes;
        try
        {
            edits();
        }
        finally
        {
            var before = _groupBefore;
            _groupBefore = null;
            if (!before.AsSpan().SequenceEqual(Bytes)) _history.Record(before, Bytes, new SpliceEdit([Difference(before, Bytes)]));
        }
    }

    /// <summary>
    /// The one splice that turns <paramref name="before"/> into <paramref name="after"/>: everything between their common
    /// start and their common end, those widened to whole UTF-8 characters so that the replaced text decodes.
    /// </summary>
    private static Splice Difference(byte[] before, byte[] after)
    {
        var start = 0;
        var shorter = Math.Min(before.Length, after.Length);
        while (start < shorter && before[start] == after[start]) start++;
        while (start > 0 && IsContinuation(before, start)) start--;

        var end = 0;
        while (end < shorter - start && before[^(end + 1)] == after[^(end + 1)]) end++;
        while (end > 0 && IsContinuation(before, before.Length - end)) end--;

        var text = System.Text.Encoding.UTF8.GetString(after, start, after.Length - end - start);
        return new Splice(SpliceOperation.ReEmitLine, start, before.Length - end, text);

        static bool IsContinuation(byte[] bytes, int index) => index < bytes.Length && (bytes[index] & 0xC0) == 0x80;
    }

    /// <summary>
    /// Undoes the most recent edit. <paramref name="current"/> is the file as it is now on disk or in
    /// another editor's buffer; when it differs from what the history expects, the undo is refused
    /// and nothing is written.
    /// </summary>
    internal UndoResult Undo(byte[]? current = null)
    {
        if (!_history.CanUndo) return new UndoResult.Refused("There is nothing to undo.");
        var entry = _history.PeekUndo();
        if (!Matches(current ?? Bytes, entry.AfterDigest)) return new UndoResult.Refused(DriftUndo);
        _history.Undone();
        Replace(entry.Snapshot ?? SpliceEdit.Apply(Bytes, entry.Inverse));
        return new UndoResult.Done(entry.Inverse);
    }

    /// <summary>Redoes the most recently undone edit, refused on drift as <see cref="Undo"/> is.</summary>
    internal UndoResult Redo(byte[]? current = null)
    {
        if (!_history.CanRedo) return new UndoResult.Refused("There is nothing to redo.");
        var entry = _history.PeekRedo();
        if (!Matches(current ?? Bytes, entry.BeforeDigest)) return new UndoResult.Refused(DriftRedo);
        _history.Redone();
        Replace(SpliceEdit.Apply(Bytes, entry.Edit.Splices));
        return new UndoResult.Done(entry.Edit.Splices);
    }

    /// <summary>Replaces the bytes after an external change the host has accepted (FBL §7.3): reads again and clears the history.</summary>
    public void Reload(byte[] bytes)
    {
        _history.Clear();
        Replace(bytes);
    }

    /// <summary>Called whenever the bytes change, to read them again.</summary>
    protected abstract void Reread();

    private void Replace(byte[] bytes)
    {
        Bytes = bytes;
        Reread();
    }

    private static bool Matches(byte[] bytes, byte[] digest) => SHA256.HashData(bytes).AsSpan().SequenceEqual(digest);
}
