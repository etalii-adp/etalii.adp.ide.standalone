using System.Text;

namespace EtAlii.Adp.Specification.Fbl.Grammar;

/// <summary>
/// The items <c>split</c> cuts a slot's value into (FBL 0.13 §4.9, <i>Several items in one node</i>): the value is cut at
/// every occurrence of the split, from the left, and each piece without the white space at its start and its end (Unicode's
/// <c>White_Space</c> property) is an item, with its span in the body; a piece that is then empty is no item.
/// </summary>
internal static class SplitItems
{
    /// <summary>The items of <paramref name="value"/>, which stands in the body from the byte <paramref name="start"/>.</summary>
    public static IReadOnlyList<(string Text, Span Span)> Cut(string value, int start, string split)
    {
        var items = new List<(string Text, Span Span)>();
        var from = 0;
        while (from <= value.Length)
        {
            var at = value.IndexOf(split, from, StringComparison.Ordinal);
            var end = at < 0 ? value.Length : at;
            if (Trimmed(value, from, end) is { } piece)
            {
                var offset = start + Encoding.UTF8.GetByteCount(value.AsSpan(0, piece.From));
                items.Add((value[piece.From..piece.To], new Span(offset, offset + Encoding.UTF8.GetByteCount(value.AsSpan(piece.From, piece.To - piece.From)))));
            }
            if (at < 0) break;
            from = at + split.Length;
        }
        return items;
    }

    /// <summary>The piece from <paramref name="from"/> to <paramref name="to"/> without its white space; null when nothing else is left.</summary>
    private static (int From, int To)? Trimmed(string value, int from, int to)
    {
        while (from < to && Rune.DecodeFromUtf16(value.AsSpan(from, to - from), out var rune, out var length) == System.Buffers.OperationStatus.Done && Rune.IsWhiteSpace(rune)) from += length;
        while (to > from && Rune.DecodeLastFromUtf16(value.AsSpan(from, to - from), out var rune, out var length) == System.Buffers.OperationStatus.Done && Rune.IsWhiteSpace(rune)) to -= length;
        return from < to ? (from, to) : null;
    }
}
