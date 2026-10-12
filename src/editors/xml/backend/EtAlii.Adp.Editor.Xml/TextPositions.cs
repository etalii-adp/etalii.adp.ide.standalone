namespace EtAlii.Adp.Editor.Xml;

/// <summary>
/// Turns the line and column a reader reports into an offset into the text as sent. A reader
/// counts <c>\r\n</c>, <c>\r</c> and <c>\n</c> each as one line break, whatever the text holds,
/// so the line starts are found the same way; columns are UTF-16 units from 1, as offsets are.
/// </summary>
internal sealed class TextPositions
{
    private readonly string _text;
    private readonly List<int> _lineStarts = [0];

    public TextPositions(string text)
    {
        _text = text;
        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] == '\r' && index + 1 < text.Length && text[index + 1] == '\n')
            {
                index++;
            }

            if (text[index] is '\r' or '\n')
            {
                _lineStarts.Add(index + 1);
            }
        }
    }

    /// <summary>The offset of <paramref name="line"/> and <paramref name="column"/>, both from 1, kept inside the text.</summary>
    public int Offset(int line, int column)
    {
        if (line < 1)
        {
            return 0;
        }

        var start = _lineStarts[Math.Min(line, _lineStarts.Count) - 1];
        return Math.Clamp(start + Math.Max(column, 1) - 1, 0, _text.Length);
    }

    /// <summary>
    /// The range a finding at <paramref name="offset"/> underlines: the name or word that starts
    /// there, or one character, or nothing at the end of the text.
    /// </summary>
    public (int From, int To) RangeAt(int offset)
    {
        var end = offset;
        while (end < _text.Length && IsNameCharacter(_text[end]))
        {
            end++;
        }

        if (end == offset && offset < _text.Length)
        {
            end++;
        }

        return (offset, end);
    }

    /// <summary>The range of the name that starts at <paramref name="line"/> and <paramref name="column"/>.</summary>
    public (int From, int To) NameAt(int line, int column) => RangeAt(Offset(line, column));

    private static bool IsNameCharacter(char character) =>
        char.IsLetterOrDigit(character) || character is ':' or '_' or '-' or '.';
}
