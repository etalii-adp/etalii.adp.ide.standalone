using EtAlii.Adp.Documents;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.Rdf.Tests;

/// <summary>
/// The line store on bytes alone: parse-and-reassemble identity under every line-ending
/// convention, and the unterminated-end bookkeeping that keeps splices reversible
/// (rdf-diagram Requirement 1.3). The line store is core's <see cref="LineDocument"/>, which
/// replaced this module's own copy (backend-centralization Requirement 1.1).
/// </summary>
public class RdfDocumentTests
{
    private static string Fixture(string name) =>
        IoPath.Combine(AppContext.BaseDirectory, "Fixtures", name);

    [Theory]
    [InlineData("one\r\ntwo\r\n")]
    [InlineData("one\ntwo\n")]
    [InlineData("one\r\ntwo")]
    [InlineData("one\ntwo\r\nthree")]
    [InlineData("")]
    [InlineData("\n")]
    [InlineData("just one line, no newline")]
    public void ParseAndText_RoundTripByteIdentically(string text)
    {
        // Arrange & act.
        var document = LineDocument.Parse(text);

        // Assert.
        Assert.Equal(text, document.Text);
    }

    [Fact]
    public void InsertingAtAnUnterminatedEnd_MovesTheMissingTerminator_AndIsReversible()
    {
        // Arrange.
        var document = LineDocument.Parse("a\r\nb");

        // Act.
        document.Insert(2, ["c"]);

        // Assert.
        // The old last line gained a terminator; the new last line inherited its absence.
        Assert.Equal("a\r\nb\r\nc", document.Text);

        // Act: the inverse splice.
        document.Remove(new LineRange(2, 2));

        // Assert: removing what was appended returns the original bytes.
        Assert.Equal("a\r\nb", document.Text);
    }

    [Fact]
    public void RemovingTheLastLine_PassesItsEndingToTheLineBefore()
    {
        // Arrange.
        var document = LineDocument.Parse("a\r\nb\r\nc");

        // Act.
        document.Remove(new LineRange(2, 2));

        // Assert.
        // The file ended without a newline; that is a property of the file, not of line "c".
        Assert.Equal("a\r\nb", document.Text);
    }

    [Fact]
    public void ReplacingAMiddleLine_TouchesNoOtherLine()
    {
        // Arrange.
        var document = LineDocument.Parse("a\r\nb\nc\r\n");

        // Act.
        document.Replace(new LineRange(1, 1), ["B"]);

        // Assert.
        // The replaced line keeps the ending the old one had, mixed conventions and all.
        Assert.Equal("a\r\nB\nc\r\n", document.Text);
    }

    [Fact]
    public void ATiedFile_GivesAnInsertedLineCrlf()
    {
        // Arrange.
        // As many LF endings as CRLF, so neither is the majority and the tie rule alone decides
        // which ending a new triple's line takes (backend-centralization Requirement 1.2).
        var text = File.ReadAllText(Fixture("tied-line-endings.ttl"));
        var document = LineDocument.Parse(text);
        var endings = document.Lines.Select(line => line.Ending).ToList();
        Assert.Equal(endings.Count(ending => ending == "\n"), endings.Count(ending => ending == "\r\n"));

        // Act.
        document.Insert(2, ["ex:g ex:h ex:i ."]);

        // Assert.
        Assert.Equal("\r\n", document.Lines[2].Ending);
        document.Remove(new LineRange(2, 2));
        Assert.Equal(text, document.Text);
    }
}
