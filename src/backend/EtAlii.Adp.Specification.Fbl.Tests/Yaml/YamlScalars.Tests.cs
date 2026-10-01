using EtAlii.Adp.Specification.Fbl.Yaml;
using Xunit;

namespace EtAlii.Adp.Specification.Fbl.Tests.Yaml;

/// <summary>
/// FBL §6.3, yaml scalars: "A string is plain-safe when it is not empty; has no leading or trailing
/// whitespace; contains no line break or control character; does not start with any of
/// <c>- ? : , [ ] { } # &amp; * ! | &gt; ' " % @ `</c>; contains neither <c>: </c> nor <c> #</c> and does
/// not end with <c>:</c>; and would be read back as the same string under the YAML 1.2 core schema
/// and as a string under YAML 1.1 (so not null, ~, true, false, yes, no, on, off, y, n in any case, a
/// number, or a date or date-time), unless the attribute's DISL type is the type it would read as."
/// </summary>
public class YamlScalarsTests
{
    [Theory]
    [InlineData("Discovery", true)]
    [InlineData("Plan the launch", true)]
    [InlineData("a-b:c", true)]
    [InlineData("", false)]
    [InlineData(" lead", false)]
    [InlineData("trail ", false)]
    [InlineData("two\nlines", false)]
    [InlineData("bell\u0007", false)]
    [InlineData("- item", false)]
    [InlineData("?q", false)]
    [InlineData(":x", false)]
    [InlineData(",x", false)]
    [InlineData("[x", false)]
    [InlineData("]x", false)]
    [InlineData("{x", false)]
    [InlineData("}x", false)]
    [InlineData("#x", false)]
    [InlineData("&x", false)]
    [InlineData("*x", false)]
    [InlineData("!x", false)]
    [InlineData("|x", false)]
    [InlineData(">x", false)]
    [InlineData("'x", false)]
    [InlineData("\"x", false)]
    [InlineData("%x", false)]
    [InlineData("@x", false)]
    [InlineData("`x", false)]
    [InlineData("key: value", false)]
    [InlineData("text #comment", false)]
    [InlineData("ends:", false)]
    [InlineData("null", false)]
    [InlineData("~", false)]
    [InlineData("True", false)]
    [InlineData("FALSE", false)]
    [InlineData("yes", false)]
    [InlineData("No", false)]
    [InlineData("on", false)]
    [InlineData("OFF", false)]
    [InlineData("y", false)]
    [InlineData("N", false)]
    [InlineData("42", false)]
    [InlineData("-1.5e3", false)]
    [InlineData("0x1F", false)]
    [InlineData("1_000", false)]
    [InlineData("12:30", false)]
    [InlineData("2026-10-01", false)]
    [InlineData("2026-10-01T09:00:00", false)]
    public void APlainSafeStringIsWrittenPlain(string value, bool safe)
    {
        // Act and assert.
        Assert.Equal(safe, YamlScalars.IsPlainSafe(value, timeTyped: false));
    }

    [Theory]
    [InlineData("2026-10-01")]
    [InlineData("2026-10-01T09:00:00")]
    public void ADateIsPlainSafeWhenTheAttributeIsADate(string value)
    {
        // Act and assert: "unless the attribute's DISL type is the type it would read as".
        Assert.True(YamlScalars.IsPlainSafe(value, timeTyped: true));
    }

    [Theory]
    [InlineData("a\"b\\c\n", "\"a\\\"b\\\\c\\n\"")]
    [InlineData("tab\t", "\"tab\\t\"")]
    public void ADoubleQuotedStringEscapesBackslashQuoteAndControlCharacters(string value, string written)
    {
        // Act and assert.
        Assert.Equal(written, YamlScalars.DoubleQuoted(value));
    }

    [Fact]
    public void ASingleQuotedStringDoublesItsQuotes()
    {
        // Act and assert.
        Assert.Equal("'it''s'", YamlScalars.SingleQuoted("it's"));
    }
}
