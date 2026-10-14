namespace EtAlii.Adp.Diagram.CausalLoopDiagram.Tests.Parity;

/// <summary>One line of a <c>.cld</c> document, with the ending it arrived with.</summary>
/// <remarks>The parity oracle (causal-loop-disl-fbl task 7): the module's hand-written reading and writing, kept in the test project to hold the definition and binding to what the tool did, and used by no product code.</remarks>
/// <param name="Text">The line without its ending.</param>
/// <param name="Ending">Its own ending - <c>\r\n</c>, <c>\n</c>, or empty for an unterminated last line.</param>
internal sealed record CausalLoopLine(string Text, string Ending)
{
    /// <summary>Whether the line states nothing: blank, or only whitespace.</summary>
    public bool IsBlank => string.IsNullOrWhiteSpace(Text);

    /// <summary>Whether the line is a comment. A <c>#</c> after leading whitespace, as the shell family writes them.</summary>
    public bool IsComment => Text.TrimStart().StartsWith('#');
}
