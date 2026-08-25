namespace EtAlii.Adp.Diagram.AzurePipeline;

/// <summary>
/// One line of a pipeline document, exactly as it was read.
/// </summary>
/// <remarks>
/// <paramref name="Ending"/> is that line's own terminator rather than the document's, because a
/// file is not obliged to be consistent: a document edited on two machines can carry both, and
/// rewriting one line must not silently convert the others. The last line of a file with no
/// trailing newline carries an empty ending, which is what lets a round trip reproduce it.
/// </remarks>
/// <param name="Text">The line without its terminator, indentation and trailing spaces included.</param>
/// <param name="Ending">This line's terminator: <c>"\r\n"</c>, <c>"\n"</c>, or empty at end of file.</param>
public sealed record PipelineLine(string Text, string Ending)
{
    /// <summary>The number of leading spaces, which is what YAML nesting is expressed in.</summary>
    public int Indent => Text.Length - Text.TrimStart(' ').Length;

    /// <summary>Whether the line carries nothing but whitespace.</summary>
    public bool IsBlank => Text.Trim().Length == 0;

    /// <summary>Whether the line's first non-space character starts a comment.</summary>
    public bool IsComment => Text.TrimStart(' ').StartsWith('#');

    /// <summary>The line and its terminator, which is what a round trip writes back.</summary>
    public override string ToString() => Text + Ending;
}
