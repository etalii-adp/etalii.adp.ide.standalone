namespace EtAlii.Adp.C4;

/// <summary>
/// A Structurizr DSL document, held as the exact lines it was read as. Nothing here parses the
/// model - that is <see cref="C4ModelParser"/>'s job - because the guarantee this type exists
/// for is textual: a document ADP did not change comes back byte-identical, and one it did
/// change differs only in the lines the edit touched (c4-diagrams Requirements 3.1, 3.2).
/// </summary>
/// <remarks>
/// The same discipline <c>MindmapDocument</c> arrived at for <c>.mm</c>: keep the original,
/// edit lines in place, and never regenerate text that nobody asked to change. Block comments
/// are deliberately *not* stripped structurally - they are ordinary text this type carries
/// through untouched, and the parser skips them.
/// </remarks>
public sealed class C4Document
{
    private readonly List<string> _lines;

    private C4Document(List<string> lines, string newline, bool endsWithNewline)
    {
        _lines = lines;
        Newline = newline;
        EndsWithNewline = endsWithNewline;
    }

    /// <summary>The line terminator the document was written with, reused for every line this writes.</summary>
    public string Newline { get; }

    /// <summary>Whether the document's last line was terminated - a file that ends without one must keep ending without one.</summary>
    public bool EndsWithNewline { get; }

    /// <summary>The lines, in order, without terminators.</summary>
    public IReadOnlyList<string> Lines => _lines;

    /// <summary>The lines paired with their 1-based numbers, which is what the parsers read.</summary>
    public IEnumerable<C4Line> CodeLines => _lines.Select((text, index) => new C4Line(text, (uint)(index + 1)));

    /// <summary>
    /// <paramref name="text"/> split into lines, remembering how it was terminated. Mixed
    /// terminators are tolerated - the dominant one is reused for new lines - because a
    /// document edited on two platforms is a normal thing to find in a repository.
    /// </summary>
    public static C4Document Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var newline = DetectNewline(text);
        var endsWithNewline = text.EndsWith('\n');
        var body = endsWithNewline ? text[..^(text.EndsWith("\r\n", StringComparison.Ordinal) ? 2 : 1)] : text;
        var lines = body.Length == 0 && endsWithNewline
            ? [string.Empty]
            : SplitLines(body);

        return new C4Document(lines, newline, endsWithNewline);
    }

    /// <summary>The document as text, byte-identical to what <see cref="Parse"/> was given when nothing was edited.</summary>
    public string ToText()
    {
        var text = string.Join(Newline, _lines);
        return EndsWithNewline ? text + Newline : text;
    }

    /// <summary>
    /// Replaces line <paramref name="number"/> (1-based) with <paramref name="text"/>. Every
    /// other line keeps the bytes it arrived as, which is the whole of Requirement 3.2.
    /// </summary>
    public void ReplaceLine(uint number, string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        EnsureInRange(number);
        _lines[(int)number - 1] = text;
    }

    /// <summary>Inserts <paramref name="text"/> so that it becomes line <paramref name="number"/>.</summary>
    public void InsertLine(uint number, string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (number < 1 || number > _lines.Count + 1)
        {
            throw new ArgumentOutOfRangeException(nameof(number), number, $"The document has {_lines.Count} lines.");
        }

        _lines.Insert((int)number - 1, text);
    }

    /// <summary>Removes lines <paramref name="from"/> to <paramref name="to"/> inclusive, 1-based.</summary>
    public void RemoveLines(uint from, uint to)
    {
        EnsureInRange(from);
        EnsureInRange(to);
        if (to < from)
        {
            throw new ArgumentOutOfRangeException(nameof(to), to, "The last line to remove precedes the first.");
        }

        _lines.RemoveRange((int)from - 1, (int)(to - from + 1));
    }

    private void EnsureInRange(uint number)
    {
        if (number < 1 || number > _lines.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(number), number, $"The document has {_lines.Count} lines.");
        }
    }

    /// <summary>
    /// The terminator to reuse: whichever of CRLF and LF occurs more often, and the platform's
    /// own when the document has no line breaks at all to learn from.
    /// </summary>
    private static string DetectNewline(string text)
    {
        var crlf = 0;
        var lf = 0;
        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] != '\n')
            {
                continue;
            }

            if (index > 0 && text[index - 1] == '\r')
            {
                crlf++;
            }
            else
            {
                lf++;
            }
        }

        if (crlf == 0 && lf == 0)
        {
            return Environment.NewLine;
        }

        return crlf >= lf ? "\r\n" : "\n";
    }

    private static List<string> SplitLines(string body)
    {
        var lines = new List<string>();
        var start = 0;
        for (var index = 0; index < body.Length; index++)
        {
            if (body[index] != '\n')
            {
                continue;
            }

            var end = index > start && body[index - 1] == '\r' ? index - 1 : index;
            lines.Add(body[start..end]);
            start = index + 1;
        }

        lines.Add(body[start..]);
        return lines;
    }
}
