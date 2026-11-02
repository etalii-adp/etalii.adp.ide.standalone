using EtAlii.Adp.Documents;

namespace EtAlii.Adp.Diagram.C4;

/// <summary>
/// The DSL's line lexer, which tells <see cref="C4Line.Code"/> a comment from a <c>//</c> inside a quoted string.
/// </summary>
/// <remarks>
/// Only <see cref="AddC4ViewCommand"/> still reads lines, because a view is code and not a model change of the binding
/// (c4-disl-fbl Requirement 8); every other edit is written by the library (task 15), and the tokenizer and the
/// argument surgery the commands used before then are kept with the tests' oracle, <c>C4LineSurgery</c>.
/// </remarks>
public static class C4Tokens
{
    /// <summary>
    /// The DSL's line lexer: <c>//</c> and <c>#</c> comments outside quoted strings, <c>\"</c> for a
    /// quote inside one, and braces as tokens of their own.
    /// </summary>
    internal static readonly LineLexer Lexer = new(new CommentPolicy(["#", "//"], OutsideQuotesOnly: true), EscapePolicy.BackslashQuote, "{}");
}
