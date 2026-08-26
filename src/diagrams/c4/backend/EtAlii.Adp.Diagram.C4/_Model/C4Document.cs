namespace EtAlii.Adp.Diagram.C4;

/// <summary>
/// A Structurizr DSL document, held as the exact lines it was read as. Nothing here parses the
/// model - because the guarantee this type exists for is textual: a document ADP did not change comes back byte-identical,
/// and one it did change differs only in the lines the edit touched (c4-diagrams Requirements 3.1, 3.2).
/// </summary>
/// <remarks>
/// The same discipline <c>MindmapDocument</c> arrived at for <c>.mm</c>: keep the original,
/// edit lines in place, and never regenerate text that nobody asked to change. Block comments
/// are deliberately *not* stripped structurally - they are ordinary text this type carries
/// through untouched, and the parser skips them.
/// </remarks>
public sealed class C4Document
{
    /// <summary>
    /// One line as it was read: its text, and the terminator that followed it. Empty for the
    /// last line of a file that ends without one.
    /// </summary>
    /// <remarks>
    /// The terminator is kept per line rather than per document because a document edited on two
    /// platforms carries both, and rewriting every line to the prevailing one makes the first
    /// save a whole-file diff - exactly the diff Requirement 3.1 forbids.
    /// </remarks>
    private readonly record struct Line(string Text, string Terminator);

    private readonly List<Line> _lines;

    private C4Document(List<Line> lines, string newline, bool endsWithNewline)
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
    public IReadOnlyList<string> Lines => _lines.Select(line => line.Text).ToArray();

    /// <summary>The lines paired with their 1-based numbers, which is what the parsers read.</summary>
    public IEnumerable<C4Line> CodeLines => _lines.Select((line, index) => new C4Line(line.Text, (uint)(index + 1)));

    /// <summary>
    /// <paramref name="text"/> split into lines, each keeping the terminator that followed it.
    /// Mixed terminators are carried through unchanged - a document edited on two platforms is a
    /// normal thing to find in a repository - and only a line this type *writes* takes the
    /// document's prevailing style.
    /// </summary>
    public static C4Document Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        return new C4Document(SplitLines(text), DetectNewline(text), text.EndsWith('\n'));
    }

    /// <summary>The document as text, byte-identical to what <see cref="Parse"/> was given when nothing was edited.</summary>
    public string ToText() =>
        // Each line writes back the terminator it arrived with, so a document nobody edited comes
        // back byte-identical however many styles it mixes.
        string.Concat(_lines.Select(line => line.Text + line.Terminator));

    /// <summary>
    /// Replaces line <paramref name="number"/> (1-based) with <paramref name="text"/>. Every
    /// other line keeps the bytes it arrived as, which is the whole of Requirement 3.2.
    /// </summary>
    public void ReplaceLine(uint number, string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        EnsureInRange(number);
        _lines[(int)number - 1] = _lines[(int)number - 1] with { Text = text };
    }

    /// <summary>Inserts <paramref name="text"/> so that it becomes line <paramref name="number"/>.</summary>
    public void InsertLine(uint number, string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (number < 1 || number > _lines.Count + 1)
        {
            throw new ArgumentOutOfRangeException(nameof(number), number, $"The document has {_lines.Count} lines.");
        }

        // A new line has no terminator of its own to keep, so it takes the document's prevailing
        // style - which is what Newline was always for. Appended past the end of a file that did
        // not end with one, it inherits that: the new last line is unterminated and the line it
        // displaced gains a terminator, so the document still ends the way it did.
        var appendingToUnterminated = number == _lines.Count + 1 && !EndsWithNewline && _lines.Count > 0;
        if (appendingToUnterminated)
        {
            _lines[^1] = _lines[^1] with { Terminator = Newline };
        }

        _lines.Insert((int)number - 1, new Line(text, appendingToUnterminated ? "" : Newline));
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

    /// <summary>Every line with the terminator that followed it, so both can be written back.</summary>
    private static List<Line> SplitLines(string text)
    {
        var lines = new List<Line>();
        var start = 0;
        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] != '\n')
            {
                continue;
            }

            var end = index > start && text[index - 1] == '\r' ? index - 1 : index;
            lines.Add(new Line(text[start..end], text[end..(index + 1)]));
            start = index + 1;
        }

        // Whatever follows the last terminator. A file ending in one leaves nothing here, and
        // that absence is what `EndsWithNewline` reports - there is no phantom final line.
        if (start < text.Length)
        {
            lines.Add(new Line(text[start..], ""));
        }

        return lines;
    }
}
