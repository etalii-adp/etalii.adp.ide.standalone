using EtAlii.Adp.Documents;
using EtAlii.Adp.History;
using EtAlii.Adp.Specification.Fbl;
using EtAlii.Adp.Specification.Fbl.History;
using EtAlii.Adp.Specification.Fbl.Planning;

namespace EtAlii.Adp.Diagram;

/// <summary>
/// The edit side of a body read and written through an FBL binding: where a span lies in lines, and
/// changes the library plans as splices, one at a time or together.
/// </summary>
/// <remarks>
/// <para>
/// <b>Every write is a <see cref="ModelChange"/></b> the library plans as splices in the body's own
/// conventions (FBL section 6), so a body keeps its comments, unknown keys and blank lines because no
/// splice touches them. A body that changed is remembered in its module's <see cref="RecentBodies"/>, so
/// the store, the validator and the next command read its text without reading it again.
/// </para>
/// <para>
/// The gartner-hype-cycle-graph and timeline bodies each held this, timeline's written from the
/// other's; it moved here in code-structure T279 (FR-043, FR-052).
/// </para>
/// </remarks>
public sealed class FblBodyEdits
{
    private readonly OpenBody _body;
    private readonly RecentBodies _recent;
    private readonly TextMemo<LineIndex> _lines = new();

    // While a batch is open, the changes asked for and their edits, planned against the body as it was.
    private List<(ModelChange Change, Edit Edit)>? _batch;

    /// <summary>The edits of <paramref name="body"/>, each changed body remembered in <paramref name="recent"/>.</summary>
    public FblBodyEdits(OpenBody body, RecentBodies recent)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(recent);

        _body = body;
        _recent = recent;
    }

    /// <summary>The 0-based lines <paramref name="span"/> covers; a span counts UTF-8 bytes.</summary>
    public LineRange LinesOf(Span span)
    {
        var lines = _lines.Of(_body.Bytes, () => LineIndex.OfUtf8(_body.Bytes));
        return new LineRange(lines.LineOf(span.Start), lines.LineOf(Math.Max(span.Start, span.End - 1)));
    }

    /// <summary>Plans and applies one change; the refusal's sentence when the library refuses it.</summary>
    public EditOutcome Change(ModelChange change)
    {
        if (_batch is not null)
        {
            var planned = _body.Plan(change);
            if (planned is PlanResult.Planned { Edit: var edit }) _batch.Add((change, edit));
            return planned is PlanResult.Refused declined ? EditOutcome.Refused(declined.Reason) : EditOutcome.Applied;
        }

        var result = _body.Change(change);
        if (result is not PlanResult.Refused) _recent.Remember(_body.Fork());
        return result is PlanResult.Refused refused ? EditOutcome.Refused(refused.Reason) : EditOutcome.Applied;
    }

    /// <summary>
    /// Runs <paramref name="writes"/>, each a change to a different entry that reads only that entry,
    /// and applies them together: as one edit, read once, when their splices keep apart; else one by
    /// one, in the order they were asked for, as <see cref="Change"/> would have.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Every change is planned against the body as it was before any of them</b>, so an element is
    /// found where it was read and a refusal is the one that body gives. That is what the writes would
    /// meet one by one only when each touches its own entry alone, which is the caller's to promise.
    /// </para>
    /// <para>
    /// <b>Why</b>: applying a change reads the whole body again, so N changes one by one cost N
    /// readings, which made arranging a large graph take seconds.
    /// </para>
    /// </remarks>
    public void Batch(Action writes)
    {
        ArgumentNullException.ThrowIfNull(writes);
        if (_batch is not null) throw new InvalidOperationException("A batch is already open on this body.");

        _batch = [];
        List<(ModelChange Change, Edit Edit)> batch;
        try
        {
            writes();
        }
        finally
        {
            batch = _batch;
            _batch = null;
        }

        if (Combined(batch) is { } combined)
        {
            _body.Apply(combined);
            _recent.Remember(_body.Fork());
            return;
        }

        foreach ((ModelChange change, _) in batch)
        {
            Change(change);
        }
    }

    /// <summary>The edits of a batch as one, when no two of them touch and none needs a snapshot to undo; else null.</summary>
    private static Edit? Combined(List<(ModelChange Change, Edit Edit)> batch)
    {
        var edits = batch.Select(planned => planned.Edit).Where(edit => edit.Splices.Count > 0).ToList();
        if (edits.Count == 0 || edits.Any(edit => edit.Snapshot)) return null;

        edits.Sort((left, right) => left.Splices.Min(splice => splice.Start).CompareTo(right.Splices.Min(splice => splice.Start)));
        for (var index = 1; index < edits.Count; index++)
        {
            if (edits[index - 1].Splices.Max(splice => splice.End) >= edits[index].Splices.Min(splice => splice.Start)) return null;
        }

        return new Edit([.. edits.SelectMany(edit => edit.Splices)]);
    }
}
