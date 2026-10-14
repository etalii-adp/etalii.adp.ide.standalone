namespace EtAlii.Adp.Documents;

/// <summary>
/// One YAML file that could not be read, and why - in the parser's own words.
/// </summary>
/// <remarks>
/// Recorded rather than thrown: an unreadable file degrades that file's detail and nothing else,
/// so the reader keeps walking and the model remembers what it lost. The message is the parser's
/// verbatim, not a paraphrase: a user fixing YAML is better served by "While parsing a block
/// mapping, did not find expected key" at line 7 than by ADP's opinion of it.
/// </remarks>
/// <param name="RelativePath">The file, relative to the diagram's folder.</param>
/// <param name="Message">The parser's own message.</param>
/// <param name="Line">The 1-based line the parser blamed, or 0 when it named none.</param>
public sealed record TolerantYamlFailure(string RelativePath, string Message, uint Line);
