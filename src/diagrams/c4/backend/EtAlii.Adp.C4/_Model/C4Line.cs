namespace EtAlii.Adp.C4;

/// <summary>
/// One line of a Structurizr DSL document, as read. The document is kept as lines rather than
/// reparsed into text on save: Requirement 3.2 says an edit changes only the lines it affects,
/// which is only possible if every other line is still the exact text it arrived as.
/// </summary>
/// <param name="Text">The line without its terminator.</param>
/// <param name="Number">Its 1-based position, which problems and errors report.</param>
public readonly record struct C4Line(string Text, uint Number)
{
    /// <summary>The line with comments and surrounding whitespace removed - what the parser reads.</summary>
    public string Code => StripComment(Text).Trim();

    /// <summary>Whether the line carries nothing but whitespace and comments.</summary>
    public bool IsBlank => Code.Length == 0;

    /// <summary>
    /// <paramref name="text"/> up to the first comment that starts outside a quoted string.
    /// Both line-comment forms the DSL accepts are honoured, and a <c>//</c> or <c>#</c> inside
    /// a name - a URL in a description, say - is not mistaken for one.
    /// </summary>
    public static string StripComment(string text)
    {
        var inQuotes = false;
        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            if (character == '"' && (index == 0 || text[index - 1] != '\\'))
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (inQuotes)
            {
                continue;
            }

            if (character == '#' || (character == '/' && index + 1 < text.Length && text[index + 1] == '/'))
            {
                return text[..index];
            }
        }

        return text;
    }
}
