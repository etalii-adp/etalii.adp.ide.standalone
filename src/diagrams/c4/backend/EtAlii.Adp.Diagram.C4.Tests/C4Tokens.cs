using EtAlii.Adp.Documents;

namespace EtAlii.Adp.Diagram.C4.Tests;

/// <summary>
/// The DSL's line lexer, which tells <see cref="C4Line.Code"/> a comment from a <c>//</c> inside a quoted string.
/// </summary>
/// <remarks>
/// The tests' oracle, beside <c>C4LineSurgery</c> and <c>C4Parser</c>: the product reads the body through the binding, and
/// the one edit still written by line keeps a lexer of its own in <c>C4ViewBlock</c> (c4-disl-fbl tasks 15 and 17).
/// </remarks>
public static class C4Tokens
{
    /// <summary>
    /// The DSL's line lexer: <c>//</c> and <c>#</c> comments outside quoted strings, <c>\"</c> for a
    /// quote inside one, and braces as tokens of their own.
    /// </summary>
    internal static readonly LineLexer Lexer = new(new CommentPolicy(["#", "//"], OutsideQuotesOnly: true), EscapePolicy.BackslashQuote, "{}");
}
