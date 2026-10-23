using EtAlii.Adp.History;
using EtAlii.Adp.Specification.Fbl;
using EtAlii.Adp.Specification.Fbl.Planning;

namespace EtAlii.Adp.Diagram.FunctionalDecompositionGraph;

/// <summary>
/// Removes entries as the tool always has: each entry's own lines, keeping a comment line directly above it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Stands in for gap 5</b> of the companion (finding B15, Q2 of functional-decomposition-graph-disl-fbl): FBL
/// removes an entry's line span, which reaches up over the comment lines directly above it, and a binding cannot
/// say that the comment stays. Each removal is planned by the library (<see cref="FdgBody.Plan"/>), and each splice
/// that removes an entry starts again past the comment lines its span begins with; nothing else of the plan is
/// changed. It is deleted when FBL can state a removal that leaves the comment above.
/// </para>
/// <para>
/// <b>The removals are one edit</b>: an element and the connections that name it are planned against the body as
/// it is, and their splices, each of which only takes bytes out, are applied together, so a cascade the binding
/// plans and a removal the definition asks for of the same connection take it once.
/// </para>
/// </remarks>
internal static class FdgCommentKeeper
{
    /// <summary>Removes the entries <paramref name="removals"/> name from <paramref name="body"/>, in one edit, or says why not.</summary>
    public static EditOutcome Remove(FdgBody body, IReadOnlyList<ModelChange.Remove> removals)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(removals);

        var bytes = body.Bytes;
        var spans = new List<(int Start, int End)>();
        foreach (var removal in removals)
        {
            switch (body.Plan(removal))
            {
                case PlanResult.Refused refused:
                    return EditOutcome.Refused(FdgReaderSentences.RefusalOf(refused.Reason));
                case PlanResult.Planned planned:
                    foreach (var splice in planned.Edit.Splices)
                    {
                        if (splice.Text.Length > 0) throw new InvalidOperationException($"A removal was planned as a splice that writes: {splice}.");
                        spans.Add((splice.Operation == SpliceOperation.RemoveEntry ? PastComments(bytes, splice.Start, splice.End) : splice.Start, splice.End));
                    }

                    break;
            }
        }

        body.Apply(new Edit([.. Merged(spans).Select(span => new Splice(SpliceOperation.RemoveEntry, span.Start, span.End, ""))]));
        return EditOutcome.Applied;
    }

    /// <summary>
    /// Where a removal of the bytes <paramref name="start"/> to <paramref name="end"/> starts once the comment lines it
    /// begins with are left in place. A span that begins with the line ending before it, as the last entry of a body
    /// without a final newline is removed, keeps taking a line ending: the one before the entry.
    /// </summary>
    private static int PastComments(byte[] bytes, int start, int end)
    {
        var ending = EndingAt(bytes, start);
        var line = start + ending;
        var kept = line;
        while (kept < end && IsComment(bytes, kept, end))
        {
            kept = NextLine(bytes, kept, end);
        }

        if (kept == line) return start;
        if (ending == 0) return kept;

        // The entry's own line ending is the comment's, before it.
        return kept - (kept >= 2 && bytes[kept - 2] == (byte)'\r' ? 2 : 1);
    }

    /// <summary>How many bytes of a line ending begin at <paramref name="at"/>: 2, 1 or 0.</summary>
    private static int EndingAt(byte[] bytes, int at) =>
        at < bytes.Length && bytes[at] == (byte)'\r' && at + 1 < bytes.Length && bytes[at + 1] == (byte)'\n' ? 2
        : at < bytes.Length && bytes[at] == (byte)'\n' ? 1
        : 0;

    /// <summary>Whether the line that begins at <paramref name="at"/> holds nothing but a comment.</summary>
    private static bool IsComment(byte[] bytes, int at, int end)
    {
        var index = at;
        while (index < end && bytes[index] is (byte)' ' or (byte)'\t') index++;
        return index < end && bytes[index] == (byte)'#';
    }

    /// <summary>Where the line after the one that begins at <paramref name="at"/> begins; <paramref name="end"/> when it is past it.</summary>
    private static int NextLine(byte[] bytes, int at, int end)
    {
        var index = Array.IndexOf(bytes, (byte)'\n', at, end - at);
        return index < 0 ? end : index + 1;
    }

    /// <summary>The spans in order, those that overlap or touch made one.</summary>
    private static List<(int Start, int End)> Merged(List<(int Start, int End)> spans)
    {
        var merged = new List<(int Start, int End)>();
        foreach (var span in spans.Where(span => span.End > span.Start).OrderBy(span => span.Start))
        {
            if (merged.Count > 0 && span.Start <= merged[^1].End)
            {
                merged[^1] = (merged[^1].Start, Math.Max(merged[^1].End, span.End));
            }
            else
            {
                merged.Add(span);
            }
        }

        return merged;
    }
}
