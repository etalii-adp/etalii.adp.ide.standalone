using System.Text;
using System.Xml;
using System.Xml.Linq;
using EtAlii.Adp.Specification.Fbl;

namespace EtAlii.Adp.Diagram.DotNetDependencyGraph;

/// <summary>
/// The source of an XML entry, from the line information the readers load with: its line, and the bytes of its
/// start tag, which is what FBL 11.2 calls "the source span of the entry".
/// </summary>
/// <remarks>
/// <para>
/// <b>A span is in bytes and a line position is in characters</b>, so the span is found only where the file is
/// UTF-8, with or without its byte order mark, and is empty otherwise: a project file in UTF-16, which opens
/// today (finding B16), has its line and no span. Nothing is planned against a span here, since the binding is
/// read-only; it locates the entry.
/// </para>
/// </remarks>
internal static class EntrySpan
{
    private static readonly UTF8Encoding _strict = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>The 1-based line and column <paramref name="element"/> was read at; zeros without line information.</summary>
    public static (int Line, int Column) PositionOf(XElement element) =>
        element is IXmlLineInfo info && info.HasLineInfo() ? (info.LineNumber, info.LinePosition) : (0, 0);

    /// <summary>The source of <paramref name="element"/>, read from <paramref name="bytes"/>.</summary>
    public static EntrySource Of(byte[] bytes, XElement element)
    {
        (int line, int column) = PositionOf(element);
        return new EntrySource(line, StartTag(bytes, line, column));
    }

    /// <summary>
    /// The bytes of the start tag whose name begins at <paramref name="line"/> and <paramref name="column"/>, as an
    /// XML reader counts them: from its <c>&lt;</c> to its <c>&gt;</c>. Empty where the file is not UTF-8, or where
    /// nothing at that place opens a tag.
    /// </summary>
    public static Span StartTag(byte[] bytes, int line, int column)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (line <= 0 || column <= 1) return default;

        var preamble = bytes.AsSpan().StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]) ? 3 : 0;
        if (preamble == 0 && bytes.Length >= 2 && (bytes[0], bytes[1]) is (0xFF, 0xFE) or (0xFE, 0xFF)) return default;

        string text;
        try
        {
            text = _strict.GetString(bytes, preamble, bytes.Length - preamble);
        }
        catch (DecoderFallbackException)
        {
            return default;
        }

        // A file in UTF-16 without its byte order mark decodes as UTF-8 too, a NUL beside every character.
        if (text.Contains('\0', StringComparison.Ordinal)) return default;

        var open = LineStart(text, line) is { } start ? start + column - 2 : -1;
        if (open < 0 || open >= text.Length || text[open] != '<') return default;

        var close = EndOfTag(text, open);
        if (close < 0) return default;

        var from = preamble + Encoding.UTF8.GetByteCount(text.AsSpan(0, open));
        return new Span(from, from + Encoding.UTF8.GetByteCount(text.AsSpan(open, close - open)));
    }

    /// <summary>The index of the first character of the 1-based <paramref name="line"/>, a line ending as XML counts one.</summary>
    private static int? LineStart(string text, int line)
    {
        var index = 0;
        for (var current = 1; current < line; current++)
        {
            var next = text.IndexOfAny(['\r', '\n'], index);
            if (next < 0) return null;
            index = text[next] == '\r' && next + 1 < text.Length && text[next + 1] == '\n' ? next + 2 : next + 1;
        }
        return index;
    }

    /// <summary>The index just after the <c>&gt;</c> that closes the tag opened at <paramref name="open"/>, outside quoted values; -1 when none does.</summary>
    private static int EndOfTag(string text, int open)
    {
        char? quote = null;
        for (var index = open + 1; index < text.Length; index++)
        {
            var character = text[index];
            if (quote is { } inside)
            {
                if (character == inside) quote = null;
            }
            else if (character is '"' or '\'')
            {
                quote = character;
            }
            else if (character == '>')
            {
                return index + 1;
            }
        }
        return -1;
    }
}
