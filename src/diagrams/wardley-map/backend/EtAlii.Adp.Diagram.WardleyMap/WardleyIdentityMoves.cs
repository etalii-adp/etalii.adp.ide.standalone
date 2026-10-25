using EtAlii.Adp.Documents;

namespace EtAlii.Adp.Diagram.WardleyMap;

/// <summary>
/// Carries the identities of a map across an edit made in ADP: each element keeps the id it had,
/// whatever the edit did to its key (findings B9 and B10, Requirements 6.3 and 6.4).
/// </summary>
/// <remarks>
/// <para>
/// <b>By line, not by key.</b> A key is derived from what the statement says, so a rename, a move
/// of a later holder among the holders of its name and a removal of an earlier one all change a
/// key without changing which statement the element is. The statement is what the edit kept: the
/// lines the edit did not touch are matched between the text before and the text after, and a
/// run of lines it replaced is paired in order, which is how a rename rewrites a declaration and
/// every link, `evolve`, pipeline and pipeline component naming it in place. An element whose line
/// maps to the line of an element of the same kind before the edit takes that element's id; any
/// other is new and gets a fresh one.
/// </para>
/// <para>
/// So after every edit each holder of a kind and key has the ordinal of its place in document
/// order and the id it had, and a later reload, which can only read the order (ruling 7), reads
/// what the store holds.
/// </para>
/// </remarks>
internal static class WardleyIdentityMoves
{
    /// <summary>The most cells the line comparison fills before it pairs the changed lines in order instead.</summary>
    private const long MostCells = 4_000_000;

    /// <summary>
    /// The identities of <paramref name="after"/>, read from <paramref name="afterText"/>, carried
    /// from <paramref name="entries"/>, the identities of <paramref name="before"/> as
    /// <paramref name="beforeText"/> said it.
    /// </summary>
    public static IReadOnlyList<WardleyIdentityEntry> Follow(
        WardleyMap before,
        string beforeText,
        IReadOnlyList<WardleyIdentityEntry> entries,
        WardleyMap after,
        string afterText)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(beforeText);
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(after);
        ArgumentNullException.ThrowIfNull(afterText);

        var ids = new Dictionary<(string Kind, string Key), string>();
        foreach (var entry in entries)
        {
            ids.TryAdd((entry.Kind, entry.Key), entry.Id);
        }

        var byLine = new Dictionary<(string Kind, uint Line), string>();
        foreach (var holder in WardleyIdentityKeys.All(before))
        {
            if (ids.TryGetValue((holder.Kind, holder.Key), out var id))
            {
                byLine.TryAdd((holder.Kind, holder.Line), id);
            }
        }

        var lines = Lines(LinesOf(beforeText), LinesOf(afterText));
        return WardleyIdentities.Assign(
            WardleyIdentityKeys.All(after),
            holder => lines.TryGetValue(holder.Line, out var was) ? byLine.GetValueOrDefault((holder.Kind, was)) : null);
    }

    private static string[] LinesOf(string text) =>
        LineDocument.Parse(text).Lines.Select(line => line.Text).ToArray();

    /// <summary>
    /// For each 1-based line of <paramref name="after"/> that the edit kept or replaced, the
    /// 1-based line of <paramref name="before"/> it came from.
    /// </summary>
    private static Dictionary<uint, uint> Lines(IReadOnlyList<string> before, IReadOnlyList<string> after)
    {
        var map = new Dictionary<uint, uint>();

        var prefix = 0;
        while (prefix < before.Count && prefix < after.Count && before[prefix] == after[prefix])
        {
            Pair(prefix, prefix);
            prefix++;
        }

        var suffix = 0;
        while (suffix < before.Count - prefix && suffix < after.Count - prefix
               && before[before.Count - 1 - suffix] == after[after.Count - 1 - suffix])
        {
            Pair(before.Count - 1 - suffix, after.Count - 1 - suffix);
            suffix++;
        }

        var oldCount = before.Count - prefix - suffix;
        var newCount = after.Count - prefix - suffix;

        if (oldCount == newCount || (long)(oldCount + 1) * (newCount + 1) > MostCells)
        {
            // An edit that kept the number of lines replaced them in place; and one too large to
            // compare is paired in order, which is never worse than matching by key alone.
            Gap(prefix, oldCount, prefix, newCount);
            return map;
        }

        // The longest common subsequence of the middle, each run between two kept lines paired in order.
        var length = new int[oldCount + 1, newCount + 1];
        for (var i = oldCount - 1; i >= 0; i--)
        {
            for (var j = newCount - 1; j >= 0; j--)
            {
                length[i, j] = before[prefix + i] == after[prefix + j]
                    ? length[i + 1, j + 1] + 1
                    : Math.Max(length[i + 1, j], length[i, j + 1]);
            }
        }

        int oldAt = 0, newAt = 0, oldRun = 0, newRun = 0;
        while (oldAt < oldCount && newAt < newCount)
        {
            if (before[prefix + oldAt] == after[prefix + newAt])
            {
                Gap(prefix + oldRun, oldAt - oldRun, prefix + newRun, newAt - newRun);
                Pair(prefix + oldAt, prefix + newAt);
                oldRun = ++oldAt;
                newRun = ++newAt;
            }
            else if (length[oldAt + 1, newAt] >= length[oldAt, newAt + 1])
            {
                oldAt++;
            }
            else
            {
                newAt++;
            }
        }

        Gap(prefix + oldRun, oldCount - oldRun, prefix + newRun, newCount - newRun);
        return map;

        void Pair(int oldIndex, int newIndex) => map[(uint)newIndex + 1] = (uint)oldIndex + 1;

        void Gap(int oldStart, int oldLength, int newStart, int newLength)
        {
            for (var k = 0; k < Math.Min(oldLength, newLength); k++)
            {
                Pair(oldStart + k, newStart + k);
            }
        }
    }
}
