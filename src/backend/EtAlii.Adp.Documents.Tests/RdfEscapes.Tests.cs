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
}
