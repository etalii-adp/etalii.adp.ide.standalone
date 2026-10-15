namespace EtAlii.Adp.Diagram.C4;

/// <summary>
/// One line of a Structurizr DSL document, as read. The document is kept as lines rather than
/// reparsed into text on save: Requirement 3.2 says an edit changes only the lines it affects,
/// which is only possible if every other line is still the exact text it arrived as.
/// </summary>
/// <param name="Text">The line without its terminator.</param>
/// <param name="Number">Its 1-based position, which problems and errors report.</param>
public readonly record struct C4Line(string Text, uint Number)
{
    /// <summary>
    /// The line with comments and surrounding whitespace removed - what the parser reads. Both
    /// line-comment forms the DSL accepts are honoured, and a <c>//</c> or <c>#</c> inside a
    /// name - a URL in a description, say - is not mistaken for one.
    /// </summary>
    public string Code => C4Tokens.Lexer.StripComment(Text).Trim();

    /// <summary>Whether the line carries nothing but whitespace and comments.</summary>
    public bool IsBlank => Code.Length == 0;
}
