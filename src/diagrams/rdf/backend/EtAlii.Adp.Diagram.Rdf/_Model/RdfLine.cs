namespace EtAlii.Adp.Diagram.Rdf;

/// <summary>
/// One line of an RDF document: its text, and the terminator that followed it.
/// </summary>
/// <remarks>
/// The terminator is held per line rather than per document because a document may legitimately
/// mix them, and because the last line of a file that ends without a newline has none at all.
/// Both facts are things a round trip has to reproduce rather than tidy up.
/// </remarks>
/// <param name="Text">The line without its terminator.</param>
/// <param name="Ending">The terminator, <c>"\r\n"</c>, <c>"\n"</c>, or empty at an unterminated end of file.</param>
public sealed record RdfLine(string Text, string Ending)
{
    /// <summary>Whether the line carries nothing but whitespace.</summary>
    public bool IsBlank => string.IsNullOrWhiteSpace(Text);

    /// <summary>Whether the line's first non-whitespace character starts a Turtle comment.</summary>
    public bool IsComment => Text.TrimStart().StartsWith('#');
}
