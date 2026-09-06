using System.Text;
using EtAlii.Adp.Common;
using EtAlii.Adp.Hierarchy;

namespace EtAlii.Adp.Diagram.CausalLoop;

/// <summary>
/// A <c>.cld</c> document as lines, each keeping the ending it arrived with, so an edit splices
/// the lines it must and leaves every other byte exactly as the author wrote it.
/// </summary>
/// <remarks>
/// <para>
/// The format is line-oriented on purpose: one statement per line means adding a link produces a
/// one-line diff rather than a rewritten file (Requirement 1.3), which is what makes a causal
/// loop diagram reviewable in the same pull request as the code it describes.
/// </para>
/// <para>
/// Endings are kept per line rather than normalized, and a document with no lines at all takes
/// the house CRLF. A writer that reserialized from the model would be correct about the content
/// and wrong about everything else in the file.
/// </para>
/// </remarks>
public sealed class CausalLoopDocument
{
    private readonly List<CausalLoopLine> _lines;

    private CausalLoopDocument(List<CausalLoopLine> lines, string dominantEnding)
    {
        _lines = lines;
        DominantEnding = dominantEnding;
    }

    /// <summary>The lines, in order.</summary>
    public IReadOnlyList<CausalLoopLine> Lines => _lines;

    /// <summary>The ending a newly inserted line takes: whichever the document uses most, CRLF where it has none.</summary>
    public string DominantEnding { get; }

    /// <summary>Reads text into lines, preserving each line's own ending.</summary>
    public static CausalLoopDocument Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var lines = new List<CausalLoopLine>();
        var crlf = 0;
        var lf = 0;
        var start = 0;

        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] != '\n')
            {
                continue;
            }

            var hasCarriage = index > start && text[index - 1] == '\r';
            var end = hasCarriage ? index - 1 : index;
            lines.Add(new CausalLoopLine(text[start..end], hasCarriage ? "\r\n" : "\n"));
            if (hasCarriage)
            {
                crlf++;
            }
            else
            {
                lf++;
            }

            start = index + 1;
        }

        // A trailing fragment with no ending is a line too - the last one, unterminated.
        if (start < text.Length)
        {
            lines.Add(new CausalLoopLine(text[start..], ""));
        }

        return new CausalLoopDocument(lines, lf > crlf ? "\n" : "\r\n");
    }

    /// <summary>The document as text, byte for byte as it was read where nothing has been spliced.</summary>
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

    /// <summary>Replaces the lines in <paramref name="range"/> with <paramref name="replacement"/>.</summary>
    public void Replace(LineRange range, IReadOnlyList<string> replacement)
    {
        ArgumentNullException.ThrowIfNull(replacement);
        ArgumentOutOfRangeException.ThrowIfNegative(range.Start);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(range.End, _lines.Count);

        // The ending of the last replaced line is kept for the last replacement line, so
        // splicing at the end of a file without a trailing newline does not add one.
        var ending = _lines[range.End].Ending;
        var written = replacement
            .Select((text, index) => new CausalLoopLine(text, index == replacement.Count - 1 ? ending : DominantEnding))
            .ToList();

        _lines.RemoveRange(range.Start, range.Length);
        _lines.InsertRange(range.Start, written);
    }

    /// <summary>Inserts <paramref name="lines"/> before the line at <paramref name="index"/>.</summary>
    public void Insert(int index, IReadOnlyList<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(index, _lines.Count);

        // An unterminated last line has to gain an ending before anything follows it.
        if (index == _lines.Count && _lines.Count > 0 && _lines[^1].Ending.Length == 0)
        {
            _lines[^1] = _lines[^1] with { Ending = DominantEnding };
        }

        _lines.InsertRange(index, lines.Select(text => new CausalLoopLine(text, DominantEnding)));
    }

    /// <summary>Removes the lines in <paramref name="range"/>.</summary>
    public void Remove(LineRange range)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(range.Start);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(range.End, _lines.Count);

        _lines.RemoveRange(range.Start, range.Length);
    }
}
