using EtAlii.Adp.Specification.Fbl.Planning;

namespace EtAlii.Adp.Designer.Knowledge;

/// <summary>
/// The edits of one knowledge session that are shown and not written yet, and the writes that are
/// to make them: one at a time, each waiting for the one before it, in the order they were made.
/// </summary>
/// <remarks>
/// <para>
/// <b>A refused write takes back every edit still waiting</b>: each was made on top of the one that
/// failed. Taking them back raises the generation by one, so the writes already queued for them
/// find, when their turn comes, that they belong to an older generation and write nothing.
/// </para>
/// <para>
/// <b>Not thread-safe.</b> The <see cref="KnowledgeSession"/> that owns it touches it only with its
/// own lock held, together with what is shown, so the two never disagree; a lock of its own here
/// would be a second lock to order against that one.
/// </para>
/// </remarks>
internal sealed class PendingEditQueue
{
    /// <summary>The edits that are shown and not written yet, in the order they were made.</summary>
    private readonly List<PendingEdit> _pending = [];

    /// <summary>Raised by one when the pending edits are taken back, so the writes already queued for them do nothing.</summary>
    private int _generation;

    /// <summary>The edits that are shown and not written yet, in the order they were made.</summary>
    public IReadOnlyList<PendingEdit> Pending => _pending;

    /// <summary>The writes still to come, each waiting for the one before it.</summary>
    public Task Writes { get; private set; } = Task.CompletedTask;

    /// <summary>True while an edit is being written: the file is then read when that write ends.</summary>
    public bool Writing { get; private set; }

    /// <summary>How many writes have started: an external re-read that began before one is older than that write's own re-read.</summary>
    public int WritesStarted { get; private set; }

    /// <summary>Takes an edit as made and not written yet, behind those already waiting.</summary>
    /// <returns>The generation its write belongs to: the one it is written in unless it is taken back first.</returns>
    public int Add(ShortGuid editId, IReadOnlyList<ModelChange> changes, (string RowId, string ColumnId)? cell)
    {
        _pending.Add(new PendingEdit(editId, changes, cell));
        return _generation;
    }

    /// <summary>Queues a write behind those already waiting; it starts when the one before it has ended, however that ended.</summary>
    public void Enqueue(Func<Task> write) =>
        Writes = Writes.ContinueWith(_ => write(), CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default).Unwrap();

    /// <summary>A queued write's turn has come.</summary>
    /// <returns>False when its edit was taken back with an earlier one that was refused: there is nothing to write.</returns>
    public bool TryStart(int generation)
    {
        if (generation != _generation)
        {
            return false;
        }

        Writing = true;
        WritesStarted++;
        return true;
    }

    /// <summary>A write has ended: its edit is no longer waiting, and when it was refused every later edit goes with it.</summary>
    /// <returns>The ids of the later edits taken back with it, in the order they were made; null when the edit had already been taken back.</returns>
    public IReadOnlyList<ShortGuid>? Settle(ShortGuid editId, int generation, bool written)
    {
        Writing = false;
        if (generation != _generation)
        {
            return null;
        }

        IReadOnlyList<ShortGuid> takenBack = [];
        _pending.RemoveAll(pending => pending.EditId == editId);
        if (!written)
        {
            // Every edit still waiting was made on top of this one, so it goes with it.
            takenBack = [.. _pending.Select(pending => pending.EditId)];
            _pending.Clear();
            _generation++;
        }

        return takenBack;
    }

    /// <summary>The cells whose value is an edit not yet written: they are marked as such.</summary>
    public HashSet<(string RowId, string ColumnId)> UnwrittenCells() =>
        _pending.Where(pending => pending.Cell is not null).Select(pending => pending.Cell!.Value).ToHashSet();

    /// <summary>An edit that is shown and not written yet.</summary>
    /// <param name="EditId">The id its gesture arrived under.</param>
    /// <param name="Changes">What it changes in the file.</param>
    /// <param name="Cell">The cell it sets, which is marked as not written yet; null for any other edit.</param>
    internal sealed record PendingEdit(ShortGuid EditId, IReadOnlyList<ModelChange> Changes, (string RowId, string ColumnId)? Cell);
}
