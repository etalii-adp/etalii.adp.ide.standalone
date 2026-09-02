namespace EtAlii.Adp.Diagram.DependencyGraph;

/// <summary>
/// One line of a <c>.dgr</c> document: its text, and the terminator that followed it.
/// </summary>
/// <remarks>
/// The terminator is held per line rather than per document because a document may legitimately
/// mix them, and because the last line of a file that ends without a newline has none at all.
/// Both facts are things a round trip has to reproduce rather than tidy up.
/// </remarks>
/// <param name="Text">The line without its terminator.</param>
/// <param name="Ending">The terminator, <c>"\r\n"</c>, <c>"\n"</c>, or empty at an unterminated end of file.</param>
public sealed record DependencyGraphLine(string Text, string Ending)
{
    /// <summary>Whether the line carries nothing but whitespace.</summary>
    public bool IsBlank => string.IsNullOrWhiteSpace(Text);

    /// <summary>Whether the line's first non-whitespace character starts a comment.</summary>
    /// <remarks>
    /// Used when narrowing a node's line range: YAML's end marks routinely run past the last line
    /// that actually declares something, and trailing blank or comment lines swept into an
    /// element's range would be rewritten by an edit that had no business touching them.
    /// </remarks>
    public bool IsComment => Text.TrimStart().StartsWith('#');
}
