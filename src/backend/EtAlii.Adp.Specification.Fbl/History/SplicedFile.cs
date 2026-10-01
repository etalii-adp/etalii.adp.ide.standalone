using System.Security.Cryptography;

namespace EtAlii.Adp.Specification.Fbl.History;

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

    private readonly EditHistory _history = new();

    protected SplicedFile(byte[] bytes)
    {
        Bytes = bytes;
    }

    /// <summary>The file's bytes after every applied edit.</summary>
    public byte[] Bytes { get; private set; }

    public bool CanUndo => _history.CanUndo;

    public bool CanRedo => _history.CanRedo;

    /// <summary>Applies an edit planned against the current bytes, records it, and reads the file again.</summary>
    public void Apply(Edit edit)
    {
        ArgumentNullException.ThrowIfNull(edit);
        if (edit.Splices.Count == 0) return;
        var before = Bytes;
        var after = Edit.Apply(before, edit.Splices);
        _history.Record(before, after, edit);
        Replace(after);
    }

    /// <summary>
    /// Undoes the most recent edit. <paramref name="current"/> is the file as it is now on disk or in
    /// another editor's buffer; when it differs from what the history expects, the undo is refused
    /// and nothing is written.
    /// </summary>
    public UndoResult Undo(byte[]? current = null)
    {
        if (!_history.CanUndo) return new UndoResult.Refused("There is nothing to undo.");
        var entry = _history.PeekUndo();
        if (!Matches(current ?? Bytes, entry.AfterDigest)) return new UndoResult.Refused(DriftUndo);
        _history.Undone();
        Replace(entry.Snapshot ?? Edit.Apply(Bytes, entry.Inverse));
        return new UndoResult.Done(entry.Inverse);
    }

    /// <summary>Redoes the most recently undone edit, refused on drift as <see cref="Undo"/> is.</summary>
    public UndoResult Redo(byte[]? current = null)
    {
        if (!_history.CanRedo) return new UndoResult.Refused("There is nothing to redo.");
        var entry = _history.PeekRedo();
        if (!Matches(current ?? Bytes, entry.BeforeDigest)) return new UndoResult.Refused(DriftRedo);
        _history.Redone();
        Replace(Edit.Apply(Bytes, entry.Edit.Splices));
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

/// <summary>What an undo or a redo did: the splices it applied, or why it was refused.</summary>
public abstract record UndoResult
{
    public sealed record Done(IReadOnlyList<Splice> Splices) : UndoResult;

    public sealed record Refused(string Reason) : UndoResult;
}
