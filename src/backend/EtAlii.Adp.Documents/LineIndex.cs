namespace EtAlii.Adp.Documents;

/// <summary>
/// Where each line of a text starts, for turning an offset into the text into the zero-based
/// line that holds it, by binary search.
/// </summary>
/// <remarks>
/// <para>
/// A line starts at offset 0 and after every <c>\n</c>, so a CRLF line's <c>\r</c> belongs to
/// the line it ends. A text that ends in a line break has one more start than it has lines: the
/// offset of its end opens an empty line past the last, which is where a parser reports what it
/// found at the end of the file.
/// </para>
/// <para>
/// The Turtle and SPARQL tokenizers and the RDF and SHACL writers each held this search, the
/// SHACL writer as a linear scan; they moved here in code-structure T130 (FR-043).
/// </para>
/// </remarks>
public sealed class LineIndex
{
    private readonly List<int> _starts = [0];

    /// <summary>The line starts of <paramref name="text"/>.</summary>
    public LineIndex(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] == '\n')
            {
                _starts.Add(index + 1);
            }
        }
    }

    /// <summary>The line starts of <paramref name="lines"/>, the same as those of the text they make.</summary>
    internal LineIndex(IReadOnlyList<Line> lines)
    {
        var offset = 0;
        foreach (var line in lines)
        {
            offset += line.Text.Length + line.Ending.Length;
            if (line.Ending.Length > 0)
            {
                _starts.Add(offset);
            }
        }
    }

    /// <summary>The number of line starts.</summary>
    public int Count => _starts.Count;

    /// <summary>The zero-based line holding <paramref name="offset"/>; an offset past the end is on the last line.</summary>
    public int LineOf(int offset)
    {
        var low = 0;
        var high = _starts.Count - 1;
        while (low < high)
        {
            var middle = (low + high + 1) / 2;
            if (_starts[middle] <= offset)
            {
                low = middle;
            }
            else
            {
                high = middle - 1;
            }
        }

        return low;
    }

    /// <summary>The offset at which the zero-based <paramref name="line"/> starts.</summary>
    public int StartOf(int line) => _starts[line];
}
