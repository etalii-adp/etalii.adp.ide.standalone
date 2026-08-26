namespace EtAlii.Adp.Diagram.WardleyMap;

/// <summary>
/// An OnlineWardleyMaps document, held as the exact lines it was read as. Nothing here parses
/// the map - because the guarantee this type exists for is textual: a document ADP did not
/// change comes back byte-identical, and one it did change differs only in the lines the edit
/// touched (Requirements 3.1, 3.2).
/// </summary>
/// <remarks>
/// <para>
/// The same discipline <c>MindmapDocument</c> arrived at for <c>.mm</c> and <c>C4Document</c>
/// for <c>.dsl</c>. It matters more here than for either: an `.owm` is small, hand-written,
/// and read by four other tools, so a regenerated file is a diff its author has to review for
/// no reason.
/// </para>
/// <para>
/// Requirement 3.3's forward compatibility falls out of this rather than being worked for. A
/// statement this module does not model is simply a line it never splices, so a keyword from a
/// newer DSL version survives a round trip because nothing ever had the chance to drop it.
/// </para>
/// <para>
/// <b>Terminators are held per line</b>, which is where this departs from <c>C4Document</c>'s
/// shape. Joining every line with one dominant terminator loses the difference in a file that
/// mixes them - a file edited on two platforms, or merged from two branches - and Requirement
/// 3.1's promise carries no "unless the file mixes them" clause. <see cref="Newline"/> is then
/// only what a <em>new</em> line is terminated with, never what an existing one is rewritten to.
/// </para>
/// </remarks>
public sealed class WardleyDocument
{
    private readonly List<WardleyDocumentLine> _lines;

    private WardleyDocument(List<WardleyDocumentLine> lines, string newline)
    {
        _lines = lines;
        Newline = newline;
    }

    /// <summary>The terminator a line this document adds is given: whichever the file uses more often.</summary>
    public string Newline { get; }

    /// <summary>Whether the document's last line was terminated - a file that ends without one must keep ending without one.</summary>
    public bool EndsWithNewline => _lines.Count > 0 && _lines[^1].Terminator.Length > 0;

    /// <summary>The lines, in order, without terminators.</summary>
    public IReadOnlyList<string> Lines => _lines.Select(line => line.Text).ToArray();

    /// <summary>The lines paired with their 1-based numbers, which is what the parser reads.</summary>
    public IEnumerable<WardleyLine> CodeLines => _lines.Select((line, index) => new WardleyLine(line.Text, (uint)(index + 1)));

    /// <summary>
    /// <paramref name="text"/> split into lines, remembering how each one was terminated.
    /// Mixed terminators are preserved exactly rather than normalised.
    /// </summary>
    public static WardleyDocument Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        return new WardleyDocument(SplitLines(text), DetectNewline(text));
    }

    /// <summary>The document as text, byte-identical to what <see cref="Parse"/> was given when nothing was edited.</summary>
    public string ToText() => string.Concat(_lines.Select(line => line.Text + line.Terminator));

    /// <summary>
    /// Replaces line <paramref name="number"/> (1-based) with <paramref name="text"/>, keeping
    /// that line's own terminator. Every other line keeps the bytes it arrived as, which is
    /// the whole of Requirement 3.2.
    /// </summary>
    public void ReplaceLine(uint number, string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        EnsureInRange(number);

        var index = (int)number - 1;
        _lines[index] = _lines[index] with { Text = text };
    }

    /// <summary>Inserts <paramref name="text"/> so that it becomes line <paramref name="number"/>.</summary>
    /// <remarks>
    /// Appending past a final line the file left unterminated has to terminate that line, or
    /// the two would run together. That is the one case where this changes a line it was not
    /// asked to change, and it is unavoidable: the alternative is a corrupt document.
    /// </remarks>
    public void InsertLine(uint number, string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (number < 1 || number > _lines.Count + 1)
        {
            throw new ArgumentOutOfRangeException(nameof(number), number, $"The document has {_lines.Count} lines.");
        }

        var index = (int)number - 1;
        if (index == _lines.Count)
        {
            if (_lines.Count > 0 && _lines[^1].Terminator.Length == 0)
            {
                _lines[^1] = _lines[^1] with { Terminator = Newline };
            }

            _lines.Add(new WardleyDocumentLine(text, ""));
            return;
        }

        _lines.Insert(index, new WardleyDocumentLine(text, Newline));
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
    /// The terminator to give a new line: whichever of CRLF and LF occurs more often, and the
    /// platform's own when the document has no line breaks at all to learn from.
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

    /// <summary>
    /// Every line with the terminator that followed it. An empty document yields no lines at
    /// all, so that it round-trips as the empty string rather than as one blank line.
    /// </summary>
    private static List<WardleyDocumentLine> SplitLines(string text)
    {
        var lines = new List<WardleyDocumentLine>();
        if (text.Length == 0)
        {
            return lines;
        }

        var start = 0;
        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] != '\n')
            {
                continue;
            }

            var hasCarriageReturn = index > start && text[index - 1] == '\r';
            var end = hasCarriageReturn ? index - 1 : index;
            lines.Add(new WardleyDocumentLine(text[start..end], hasCarriageReturn ? "\r\n" : "\n"));
            start = index + 1;
        }

        if (start < text.Length)
        {
            lines.Add(new WardleyDocumentLine(text[start..], ""));
        }

        return lines;
    }
}
