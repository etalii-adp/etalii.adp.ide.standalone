using EtAlii.Adp.Common;
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
    /// Used when narrowing a node's line range: YAML's end marks routinely run past the last line
    /// that actually declares something, and trailing blank or comment lines swept into an
    /// element's range would be rewritten by an edit that had no business touching them.
    /// </remarks>
    public bool IsComment => Text.TrimStart().StartsWith('#');
}
