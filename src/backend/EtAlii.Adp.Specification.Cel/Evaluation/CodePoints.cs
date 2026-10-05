using System.Text;

namespace EtAlii.Adp.Specification.Cel;

/// <summary>
/// CEL counts and indexes strings by Unicode code point, and orders them code point by code point;
/// .NET strings are UTF-16, whose code units order characters beyond U+FFFF before U+E000-U+FFFF.
/// </summary>
internal static class CodePoints
{
    public static int[] Of(string text)
    {
        var points = new List<int>(text.Length);
        foreach (var rune in text.EnumerateRunes()) points.Add(rune.Value);
        return [.. points];
    }

    public static int Count(string text)
    {
        var count = 0;
        foreach (var _ in text.EnumerateRunes()) count++;
        return count;
    }

    public static string Text(ReadOnlySpan<int> points)
    {
        var builder = new StringBuilder(points.Length);
        foreach (var point in points) builder.Append(new Rune(point).ToString());
        return builder.ToString();
    }

    public static int Compare(string a, string b)
    {
        var left = a.EnumerateRunes();
        var right = b.EnumerateRunes();
        while (true)
        {
            var hasLeft = left.MoveNext();
            var hasRight = right.MoveNext();
            if (!hasLeft || !hasRight) return hasLeft.CompareTo(hasRight);
            var order = left.Current.Value.CompareTo(right.Current.Value);
            if (order != 0) return order;
        }
    }

    /// <summary>The first code point index at or after <paramref name="start"/> where <paramref name="sought"/> starts, or -1.</summary>
    public static int IndexOf(int[] text, int[] sought, int start)
    {
        for (var i = start; i + sought.Length <= text.Length; i++)
        {
            if (text.AsSpan(i, sought.Length).SequenceEqual(sought)) return i;
        }
        return -1;
    }

    /// <summary>The last code point index at or before <paramref name="start"/> where <paramref name="sought"/> starts, or -1.</summary>
    public static int LastIndexOf(int[] text, int[] sought, int start)
    {
        for (var i = Math.Min(start, text.Length - sought.Length); i >= 0; i--)
        {
            if (text.AsSpan(i, sought.Length).SequenceEqual(sought)) return i;
        }
        return -1;
    }
}
