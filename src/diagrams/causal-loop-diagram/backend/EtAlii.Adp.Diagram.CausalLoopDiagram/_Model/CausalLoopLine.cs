namespace EtAlii.Adp.Diagram.CausalLoopDiagram;

/// <summary>One line of a <c>.cld</c> document, with the ending it arrived with.</summary>
/// <param name="Text">The line without its ending.</param>
/// <param name="Ending">Its own ending - <c>\r\n</c>, <c>\n</c>, or empty for an unterminated last line.</param>
public sealed record CausalLoopLine(string Text, string Ending)
{
    /// <summary>Whether the line states nothing: blank, or only whitespace.</summary>
    public bool IsBlank => string.IsNullOrWhiteSpace(Text);

    /// <summary>Whether the line is a comment. A <c>#</c> after leading whitespace, as the shell family writes them.</summary>
    public bool IsComment => Text.TrimStart().StartsWith('#');
}
