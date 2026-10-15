using Xunit;

namespace EtAlii.Adp.Documents.Tests;

/// <summary>What a string, an IRI or a local name means once its escapes are decoded (code-structure T131, FR-044).</summary>
public class RdfEscapesTests
{
    [Theory]
    [InlineData("\"plain\"", "plain")]
    [InlineData("'single'", "single")]
    [InlineData("\"\"\"long\nform\"\"\"", "long\nform")]
    [InlineData("\"tab\\there\"", "tab\there")]
    [InlineData("\"\\b\\n\\r\\f\"", "\b\n\r\f")]
    [InlineData("\"quote \\\" inside\"", "quote \" inside")]
    [InlineData("\"caf\\u00e9\"", "café")]
    [InlineData("\"\\U0001F600\"", "\U0001F600")]
    [InlineData("\"\\q\"", "q")]
    [InlineData("\"\\u12\"", "u12")]
    public void AStringsValue_HasItsQuotesStrippedAndItsEscapesDecoded(string raw, string expected)
    {
        // Act and assert.
        Assert.Equal(expected, RdfEscapes.String(raw));
    }

    [Theory]
    [InlineData("<http://example.org/>", "http://example.org/")]
    [InlineData("<http://example.org/caf\\u00e9>", "http://example.org/café")]
    public void AnIri_DecodesOnlyItsNumericEscapes(string raw, string expected)
    {
        // Act and assert.
        Assert.Equal(expected, RdfEscapes.Iri(raw));
    }

    [Theory]
    [InlineData("plain", "plain")]
    [InlineData("a\\.b", "a.b")]
    [InlineData("a\\~b\\-c", "a~b-c")]
    public void ALocalName_DropsTheBackslashOfAReservedCharacter(string raw, string expected)
    {
        // Act and assert.
        Assert.Equal(expected, RdfEscapes.Local(raw));
    }

    [Theory]
    [InlineData("\"plain\"")]
    [InlineData("\"\\t\\b\\n\\r\\f\\\"\\'\\\\\"")]
    [InlineData("\"caf\\u00e9 \\U0001F600\"")]
    [InlineData("\"\"\"long \\\" quote\"\"\"")]
    public void EveryEscapeTheGrammarsKnow_IsNotRefused(string raw)
    {
        // Act and assert.
        Assert.Null(RdfEscapes.Refusal(raw));
    }

    [Theory]
    [InlineData("\"a\\qb\"", 2, "'\\q' is not a string escape")]
    [InlineData("\"\\u12zz\"", 1, "'\\u12zz' is not an escape")]
    [InlineData("\"\\u12\"", 1, "'\\u12' is not an escape")]
    [InlineData("\"\\UFFFFFFFF\"", 1, "'\\UFFFFFFFF' does not name a character")]
    [InlineData("\"\\uD800\"", 1, "'\\uD800' does not name a character")]
    [InlineData("\"\"\"one\ntwo \\q\"\"\"", 11, "'\\q' is not a string escape")]
    public void AnEscapeTheGrammarsRefuse_IsNamedAtItsOffset(string raw, int offset, string message)
    {
        // Act. Behaviour changes B-18: both grammars refuse it, so both parsers ask before reading.
        var refusal = RdfEscapes.Refusal(raw);

        // Assert.
        Assert.NotNull(refusal);
        Assert.Equal(offset, refusal.Value.Offset);
        Assert.StartsWith(message, refusal.Value.Message);
    }

    [Fact]
    public void AnEscapeThatNamesNoCharacter_IsReadAsItsLetter_RatherThanThrown()
    {
        // Act and assert. A caller that has not asked for the refusal still gets a value.
        Assert.Equal("UFFFFFFFF", RdfEscapes.String("\"\\UFFFFFFFF\""));
    }
}
