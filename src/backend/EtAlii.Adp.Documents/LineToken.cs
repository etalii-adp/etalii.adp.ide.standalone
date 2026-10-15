namespace EtAlii.Adp.Documents;

/// <summary>One token of a line and where it sits in that line, quotes included.</summary>
/// <param name="Value">The token's text, unquoted and unescaped.</param>
/// <param name="Start">Index of its first character in the line - the opening quote, when it has one.</param>
/// <param name="Length">How many characters it occupies, the quotes included.</param>
/// <param name="Quoted">Whether it was written in quotes.</param>
public readonly record struct LineToken(string Value, int Start, int Length, bool Quoted);
