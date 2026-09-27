namespace EtAlii.Adp.Documents;

/// <summary>
/// One line of a line-oriented document: its text, and the terminator that followed it.
/// </summary>
/// <remarks>
/// The terminator is held per line rather than per document because a document may legitimately
/// mix them, and because the last line of a file that ends without a newline has none at all.
/// Both facts are things a round trip has to reproduce rather than tidy up - and holding the
/// terminator here is what makes the two line-ending rules coexist without a caller choosing
/// between them: a rewritten line keeps this value, while new content takes
/// <see cref="AdpFileWriter.NewLine"/> (file-io-centralization Requirement 2.3).
/// </remarks>
/// <param name="Text">The line without its terminator.</param>
/// <param name="Ending">The terminator, <c>"\r\n"</c>, <c>"\n"</c>, or empty at an unterminated end of file.</param>
public sealed record Line(string Text, string Ending)
{
    /// <summary>Whether the line carries nothing but whitespace.</summary>
    public bool IsBlank => string.IsNullOrWhiteSpace(Text);

    /// <summary>Whether the line's first non-whitespace character starts a comment.</summary>
    /// <remarks>
    /// Not the rule <see cref="YamlNodeRange"/> narrows a node's range with. YAML indents with spaces
    /// alone, so there a line whose first non-space character is a tab is block-scalar content rather
    /// than a comment, and the range keeps it (backend-centralization R7.2). This general rule is
    /// the one causal-loop's own format reads.
    /// </remarks>
    public bool IsComment => Text.TrimStart().StartsWith('#');
}
