using System.Text;

namespace EtAlii.Adp.Diagram.AzurePipeline;

/// <summary>
/// A pipeline file as the lines it is made of, with edits applied by splicing one range of them.
/// </summary>
/// <remarks>
/// <para>
/// This is a <b>concrete</b> syntax tree, and the choice is the load-bearing one in this module.
/// A model-and-serialise loop discards key order, comment placement, quoting style, anchors,
/// indentation width and blank lines - all preserved by the file, none reproducible from a
/// model. For an ordinary diagram that is untidy. For an <c>azure-pipelines.yml</c> it is a diff
/// a reviewer has to read and a chance to stop the team shipping, which is why Requirement 3.1
/// asks for a byte-identical round trip rather than an equivalent one.
/// </para>
/// <para>
/// Holding the lines makes that guarantee structural rather than aspirational: a construct this
/// module does not model is never spliced, so it survives by never being touched. Requirement
/// 3.3 is satisfied by construction and not by remembering to be careful.
/// </para>
/// <para>
/// Nothing here parses YAML. <c>PipelineParser</c> reads this to build the model and
/// records which lines declare what; the writer splices those ranges back. Keeping the two apart
/// is what lets the document be tested on bytes alone.
/// </para>
/// </remarks>
public sealed class PipelineDocument
{
    private readonly List<PipelineLine> _lines;

    private PipelineDocument(List<PipelineLine> lines, string dominantEnding)
    {
        _lines = lines;
        DominantEnding = dominantEnding;
    }

    /// <summary>The document's lines, in order.</summary>
    public IReadOnlyList<PipelineLine> Lines => _lines;

    /// <summary>
    /// The terminator most of this document's lines use, which a newly inserted line adopts so
    /// an edit does not introduce a second convention into a consistent file.
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
    public static PipelineDocument Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var lines = new List<PipelineLine>();
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
            lines.Add(new PipelineLine(text[start..end], hasCarriageReturn ? "\r\n" : "\n"));
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
            lines.Add(new PipelineLine(text[start..], ""));
        }

        return new PipelineDocument(lines, crlf > lf ? "\r\n" : "\n");
    }

    /// <summary>
    /// The document as text. Byte-identical to what <see cref="Parse"/> was given for a document
    /// nothing has spliced (Requirement 3.1).
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
    /// Replaces the lines in <paramref name="range"/> with <paramref name="replacement"/>, and
    /// leaves every line outside it exactly as it was.
    /// </summary>
    /// <remarks>
    /// The replacement's terminators are supplied rather than inferred, except that a final line
    /// with no terminator inherits the one the range's last line had - so replacing the last line
    /// of a file that ends without a newline does not add one, and replacing a line in the middle
    /// does not remove one.
    /// </remarks>
    public void Replace(PipelineLineRange range, IReadOnlyList<string> replacement)
    {
        ArgumentNullException.ThrowIfNull(replacement);
        Guard(range);

        var ending = _lines[range.End].Ending;
        var lines = replacement
            .Select((text, index) => new PipelineLine(text, index == replacement.Count - 1 ? ending : DominantEnding))
            .ToList();

        _lines.RemoveRange(range.Start, range.Length);
        _lines.InsertRange(range.Start, lines);
    }

    /// <summary>Inserts lines before <paramref name="index"/>, touching no existing line.</summary>
    public void Insert(int index, IReadOnlyList<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(index, _lines.Count);

        // Inserting at the very end of a file whose last line has no terminator would otherwise
        // run the new line onto it, so that line gains one - and the *new* last line inherits the
        // missing terminator in its place.
        //
        // Moving it rather than simply adding one is what keeps an append reversible. A file with
        // no trailing newline that gained one on every append would come back one byte different
        // from an undo, and "the file is exactly as it was" is the promise this whole module is
        // built around.
        var appendingToUnterminated = index == _lines.Count && _lines.Count > 0 && _lines[^1].Ending.Length == 0;
        if (appendingToUnterminated)
        {
            _lines[^1] = _lines[^1] with { Ending = DominantEnding };
        }

        var inserted = lines.Select(text => new PipelineLine(text, DominantEnding)).ToList();
        if (appendingToUnterminated && inserted.Count > 0)
        {
            inserted[^1] = inserted[^1] with { Ending = "" };
        }

        _lines.InsertRange(index, inserted);
    }

    /// <summary>Removes the lines in <paramref name="range"/>, touching no other line.</summary>
    public void Remove(PipelineLineRange range)
    {
        Guard(range);

        // How the file ends is a property of the file, not of the line that happens to be last -
        // so when the last line goes, whatever it said about the ending passes to the line that
        // takes its place. Without this, removing an unterminated final line would silently leave
        // the file terminated, and an append followed by its own undo would not come back byte
        // for byte.
        var removingTheEnd = range.End == _lines.Count - 1;
        var ending = _lines[range.End].Ending;

        _lines.RemoveRange(range.Start, range.Length);

        if (removingTheEnd && _lines.Count > 0)
        {
            _lines[^1] = _lines[^1] with { Ending = ending };
        }
    }

    private void Guard(PipelineLineRange range)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(range.Start);
        if (range.End >= _lines.Count)
        {
            throw new ArgumentOutOfRangeException(
                nameof(range),
                $"Lines {range.Start}-{range.End} are outside a document of {_lines.Count} lines.");
        }
    }
}
