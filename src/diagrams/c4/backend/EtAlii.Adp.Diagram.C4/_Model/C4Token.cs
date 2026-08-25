namespace EtAlii.Adp.Diagram.C4;

/// <summary>One token and where it sits in the line it came from, quotes included.</summary>
/// <param name="Value">The token's text, unquoted and unescaped.</param>
/// <param name="Start">Index of its first character in the line - the opening quote, when it has one.</param>
/// <param name="Length">How many characters it occupies, the quotes included.</param>
/// <param name="Quoted">Whether it was written in quotes.</param>
public readonly record struct C4Token(string Value, int Start, int Length, bool Quoted);
