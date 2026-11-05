namespace EtAlii.Adp.Specification.Fbl.Planning;

/// <summary>
/// The difference a set of a list of items writes (FBL 0.7 §6.3, FBL 0.13 §6.3): the items of the longest common
/// subsequence of the old list and the new one stay, and the rest are changes, removals and additions.
/// </summary>
internal static class ListDifference
{
    /// <summary>The pairs of places, in <paramref name="held"/> and in <paramref name="wanted"/>, of a longest common subsequence of the two.</summary>
    public static List<(int Held, int Wanted)> CommonSubsequence(IReadOnlyList<string> held, IReadOnlyList<string> wanted)
    {
        var lengths = new int[held.Count + 1, wanted.Count + 1];
        for (var a = held.Count - 1; a >= 0; a--)
        {
            for (var b = wanted.Count - 1; b >= 0; b--)
            {
                lengths[a, b] = held[a] == wanted[b] ? lengths[a + 1, b + 1] + 1 : Math.Max(lengths[a + 1, b], lengths[a, b + 1]);
            }
        }
        var pairs = new List<(int, int)>();
        int x = 0, y = 0;
        while (x < held.Count && y < wanted.Count)
        {
            if (held[x] == wanted[y]) pairs.Add((x++, y++));
            else if (lengths[x + 1, y] >= lengths[x, y + 1]) x++;
            else y++;
        }
        return pairs;
    }
}
