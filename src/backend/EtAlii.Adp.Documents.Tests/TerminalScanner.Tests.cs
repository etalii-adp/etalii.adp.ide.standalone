using Xunit;

namespace EtAlii.Adp.Documents.Tests;

/// <summary>
/// The terminals Turtle and SPARQL share, read where the tokenizers read them
/// (code-structure T131, FR-044).
/// </summary>
public class TerminalScannerTests
{
    [Theory]
    [InlineData("  \t\r\n  ex:a", 7)]
    [InlineData("# a comment\r\nex:a", 13)]
    [InlineData("# one\n# two\n  x", 14)]
    [InlineData("x", 0)]
    [InlineData("   ", 3)]
    public void Trivia_IsWhitespaceAndCommentsUpToTheNextToken(string text, int expected)
    {
        // Act and assert.
        Assert.Equal(expected, TerminalScanner.SkipTrivia(text, 0));
    }

    [Theory]
    [InlineData("\"abc\" .", 5)]
    [InlineData("'abc' .", 5)]
    [InlineData("\"a\\\"b\" .", 6)]
    [InlineData("\"\"\"a\nb\"\"\" .", 9)]
    [InlineData("'''a \" '' b''' .", 14)]
    [InlineData("\"\" .", 2)]
    public void AString_EndsPastItsClosingQuotes(string text, int expected)
    {
        // Act.
        var scan = TerminalScanner.String(text, 0);

        // Assert.
        Assert.Equal(new TerminalScan(expected, TerminalProblem.None), scan);
    }

    [Theory]
    [InlineData("\"abc\ndef\"")]
    [InlineData("\"abc\rdef\"")]
    public void AShortString_EndsAtALineFeedOrACarriageReturn(string text)
    {
        // Act.
        var scan = TerminalScanner.String(text, 0);

        // Assert.
        // Both grammars leave either character out of a short string (behaviour changes B-02).
        Assert.Equal(TerminalProblem.LineEnded, scan.Problem);
    }

    [Theory]
    [InlineData("\"abc")]
    [InlineData("\"\"\"abc\n\"\"")]
    [InlineData("\"abc\\")]
    public void AStringTheTextEndsInside_SaysSo(string text)
    {
        // Act and assert.
        Assert.Equal(TerminalProblem.FileEnded, TerminalScanner.String(text, 0).Problem);
    }

    [Theory]
    [InlineData("<http://example.org/a> .", 22, TerminalProblem.None)]
    [InlineData("<a\\>b> .", 6, TerminalProblem.None)]
    [InlineData("<http://example.org/\n> .", 20, TerminalProblem.LineEnded)]
    [InlineData("<http://example.org/", 20, TerminalProblem.FileEnded)]
    public void AnIri_EndsPastItsClosingBracket_OnItsOwnLine(string text, int end, TerminalProblem problem)
    {
        // Act and assert.
        Assert.Equal(new TerminalScan(end, problem), TerminalScanner.Iri(text, 0));
    }

    [Theory]
    [InlineData("name .", 4)]
    [InlineData("a.b.c .", 5)]
    [InlineData("a.", 1)]
    [InlineData("a\\.b .", 4)]
    [InlineData("a%20b .", 5)]
    [InlineData("a:b;", 3)]
    public void ALocalName_TakesADotOnlyWhenTheNameGoesOn(string text, int expected)
    {
        // Act and assert.
        Assert.Equal(expected, TerminalScanner.LocalName(text, 0));
    }
}
