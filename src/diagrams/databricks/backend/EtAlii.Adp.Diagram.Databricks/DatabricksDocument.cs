using System.Text;

namespace EtAlii.Adp.Diagram.Databricks;

/// <summary>
/// A Databricks configuration file as the lines it is made of, with edits applied by splicing one
/// range of them.
/// </summary>
/// <remarks>
/// <para>
/// This is a concrete syntax tree over documents this module does <b>not</b> own:
/// <c>databricks.yml</c>, job resource YAML and pipeline settings JSON all belong to Databricks'
/// own tooling, and Requirement 2.1 says a file ADP did not change comes back byte-identical. A
/// parse-model-reserialise pipeline cannot honour that - a regenerator normalises quoting, key
/// order, indentation and blank lines on the first save - so the lines are the truth and every
/// write is a splice. YAML and JSON alike: JSON is a subset of YAML 1.2, so one line store and
/// one reading path serve all three formats.
/// </para>
/// <para>
/// Holding the lines also makes Requirement 2.4 free: a construct this module does not model is
/// never spliced, so it survives by never being touched.
/// </para>
/// <para>
/// Nothing here parses YAML. The format parsers read this to build the models and record which
/// lines declare what; the writers splice those ranges back. Keeping the two apart is what lets
/// this type be tested on bytes alone.
/// </para>
/// </remarks>
public sealed class DatabricksDocument
{
    private readonly List<DatabricksLine> _lines;

    private DatabricksDocument(List<DatabricksLine> lines, string dominantEnding)
    {
        _lines = lines;
        DominantEnding = dominantEnding;
    }

    /// <summary>The document's lines, in order.</summary>
    public IReadOnlyList<DatabricksLine> Lines => _lines;

    /// <summary>
    /// The terminator most of this document's lines use, which a newly inserted line adopts so an
    /// edit does not introduce a second convention into a consistent file.
    /// </summary>
    public string DominantEnding { get; }

    /// <summary>
    /// Splits <paramref name="text"/> into lines, keeping each line's own terminator.
    /// </summary>
    /// <remarks>
    /// A file that does not end in a newline yields a final line with an empty ending, so
    /// <see cref="Text"/> reproduces it. A file that ends in a newline does not gain a phantom
    /// empty line, which would otherwise grow the document by one line per round trip.
    /// </remarks>
    public static DatabricksDocument Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var lines = new List<DatabricksLine>();
        var start = 0;
        var crlf = 0;
        var lf = 0;

        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] != '\n')
            {
                continue;
            }

            var hasCarriageReturn = i > start && text[i - 1] == '\r';
            var end = hasCarriageReturn ? i - 1 : i;
            lines.Add(new DatabricksLine(text[start..end], hasCarriageReturn ? "\r\n" : "\n"));
            if (hasCarriageReturn)
            {
                crlf++;
            }
            else
            {
                lf++;
            }

            start = i + 1;
        }

        if (start < text.Length)
        {
            lines.Add(new DatabricksLine(text[start..], ""));
        }

        // An empty document has no evidence either way, and the repository's house style is CRLF
        // (see .gitattributes and src/.editorconfig), so a tie goes to CRLF rather than to LF.
        return new DatabricksDocument(lines, lf > crlf ? "\n" : "\r\n");
    }

    /// <summary>
    /// The document as text. Byte-identical to what <see cref="Parse"/> was given, for a document
    /// nothing has spliced (Requirement 2.1).
    /// </summary>
    public string Text
    {
        get
        {
            var builder = new StringBuilder();
            foreach (var line in _lines)
            {
                builder.Append(line.Text).Append(line.Ending);
            }

            return builder.ToString();
        }
    }

    /// <summary>
    /// Replaces the lines in <paramref name="range"/> with <paramref name="replacement"/>, leaving
    /// every line outside it exactly as it was.
    /// </summary>
    /// <remarks>
    /// The replacement's terminators are supplied rather than inferred, except that its final line
    /// inherits the one the range's last line had - so replacing the last line of a file that ends
    /// without a newline does not add one, and replacing a line in the middle does not remove one.
    /// </remarks>
    public void Replace(LineRange range, IReadOnlyList<string> replacement)
    {
        ArgumentNullException.ThrowIfNull(replacement);
        Guard(range);

        var ending = _lines[range.End].Ending;
        var lines = replacement
            .Select((text, index) => new DatabricksLine(text, index == replacement.Count - 1 ? ending : DominantEnding))
            .ToList();

        _lines.RemoveRange(range.Start, range.Length);
        _lines.InsertRange(range.Start, lines);
    }

    /// <summary>Inserts lines before <paramref name="index"/>, touching no existing line.</summary>
    /// <remarks>
    /// Appending to a file whose last line has no terminator would otherwise run the new line onto
    /// it, so that line gains one and the <em>new</em> last line inherits the missing terminator in
    /// its place. Moving it rather than simply adding one is what keeps an append reversible: a
    /// file with no trailing newline that gained one on every append would come back a byte
    /// different from its own undo. A sibling module shipped this bug before finding it, which is
    /// why <c>no-trailing-newline.yml</c> is in this module's corpus too.
    /// </remarks>
    public void Insert(int index, IReadOnlyList<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(index, _lines.Count);

        var appendingToUnterminated = index == _lines.Count && _lines.Count > 0 && _lines[^1].Ending.Length == 0;
        if (appendingToUnterminated)
        {
            _lines[^1] = _lines[^1] with { Ending = DominantEnding };
        }

        var inserted = lines.Select(text => new DatabricksLine(text, DominantEnding)).ToList();
        if (appendingToUnterminated && inserted.Count > 0)
        {
            inserted[^1] = inserted[^1] with { Ending = "" };
        }

        _lines.InsertRange(index, inserted);
    }

    /// <summary>Removes the lines in <paramref name="range"/>, touching no other line.</summary>
    /// <remarks>
    /// How a file ends is a property of the file, not of the line that happens to be last, so when
    /// the last line goes, whatever it said about the ending passes to the line that takes its
    /// place. Without this, removing an unterminated final line would silently leave the file
    /// terminated, and an append followed by its own undo would not come back byte for byte.
    /// </remarks>
    public void Remove(LineRange range)
    {
        Guard(range);

        var removingTheEnd = range.End == _lines.Count - 1;
        var ending = _lines[range.End].Ending;

        _lines.RemoveRange(range.Start, range.Length);

        if (removingTheEnd && _lines.Count > 0)
        {
            _lines[^1] = _lines[^1] with { Ending = ending };
        }
    }

    private void Guard(LineRange range)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(range.Start);
        if (range.End < range.Start)
        {
            throw new ArgumentOutOfRangeException(
                nameof(range),
                $"Line {range.End} cannot precede line {range.Start}.");
        }

        if (range.End >= _lines.Count)
        {
            throw new ArgumentOutOfRangeException(
                nameof(range),
                $"Lines {range.Start}-{range.End} are outside a document of {_lines.Count} lines.");
        }
    }
}
