using Xunit;

namespace EtAlii.Adp.Documents.Tests;

/// <summary>The line lexer every line-based module shares (code-structure T136, FR-048).</summary>
public class LineLexerTests
{
    private static readonly LineLexer _quoted = new(new CommentPolicy(["#", "//"], OutsideQuotesOnly: true), EscapePolicy.BackslashQuote, "{}");
    private static readonly LineLexer _urls = new(new CommentPolicy(["//"], OutsideQuotesOnly: false, NotAfter: ':'), EscapePolicy.None);

    [Theory]
    [InlineData("a b // note", "a b ")]
    [InlineData("a b # note", "a b ")]
    [InlineData("a \"http://x#y\" // note", "a \"http://x#y\" ")]
    [InlineData("a \"say \\\" // still text\" # note", "a \"say \\\" // still text\" ")]
    [InlineData("no comment", "no comment")]
    public void AQuoteAwarePolicy_CutsTheFirstMarkerOutsideAString(string line, string expected)
    {
        // Act and assert.
        Assert.Equal(expected, _quoted.StripComment(line));
    }

    [Theory]
    [InlineData("url http://example.org // note", "url http://example.org ")]
    [InlineData("name \"a // b\"", "name \"a ")]
    [InlineData("// all of it", "")]
    [InlineData(":///x", ":///x")]
    [InlineData("a # b", "a # b")]
    public void AQuoteBlindPolicy_CutsEveryMarkerButOneAfterItsExemptCharacter(string line, string expected)
    {
        // Act and assert.
        Assert.Equal(expected, _urls.StripComment(line));
    }

    [Fact]
    public void TheTokens_KeepAQuotedStringWhole_AndPunctuationApart_WithPositionsIntoTheLine()
    {
        // Arrange.
        const string line = "  s = system \"Internet Banking\" \"say \\\"hi\\\"\"{ // note";

        // Act.
        var tokens = _quoted.Tokens(line);

        // Assert.
        Assert.Equal(["s", "=", "system", "Internet Banking", "say \"hi\"", "{"], tokens.Select(token => token.Value));
        Assert.All(tokens, token => Assert.Equal(token.Quoted ? '"' : token.Value[0], line[token.Start]));
        Assert.Equal("\"Internet Banking\"", line.Substring(tokens[3].Start, tokens[3].Length));
        Assert.True(tokens[4].Quoted);
        Assert.False(tokens[5].Quoted);
    }

    [Fact]
    public void WithoutAnEscape_ABackslashIsText_AndAQuoteEndsTheString()
    {
        // Arrange.
        var lexer = new LineLexer(new CommentPolicy([], OutsideQuotesOnly: true), EscapePolicy.None);

        // Act.
        var tokens = lexer.Tokens("\"a\\\" b\"");

        // Assert.
        Assert.Equal(["a\\", "b", ""], tokens.Select(token => token.Value));
    }

    [Theory]
    [InlineData("plain", "\"plain\"")]
    [InlineData("", "\"\"")]
    [InlineData("say \"hi\"", "\"say \\\"hi\\\"\"")]
    public void AQuotedString_IsWrittenWithItsQuotesEscaped(string value, string expected)
    {
        // Act and assert.
        Assert.Equal(expected, QuotedString.Write(value));
    }
}
