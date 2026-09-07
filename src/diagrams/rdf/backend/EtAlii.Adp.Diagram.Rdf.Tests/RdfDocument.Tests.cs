using EtAlii.Adp.Common;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Hierarchy;
using Xunit;

namespace EtAlii.Adp.Diagram.Rdf.Tests;

/// <summary>
/// The line store on bytes alone: parse-and-reassemble identity under every line-ending
/// convention, and the unterminated-end bookkeeping that keeps splices reversible
/// (rdf-diagram Requirement 1.3).
/// </summary>
public class RdfDocumentTests
{
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
        var document = RdfDocument.Parse(text);

        // Assert.
        Assert.Equal(text, document.Text);
    }

    [Fact]
    public void InsertingAtAnUnterminatedEnd_MovesTheMissingTerminator_AndIsReversible()
    {
        // Arrange.
        var document = RdfDocument.Parse("a\r\nb");

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
        var document = RdfDocument.Parse("a\r\nb\r\nc");

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
        var document = RdfDocument.Parse("a\r\nb\nc\r\n");

        // Act.
        document.Replace(new LineRange(1, 1), ["B"]);

        // Assert.
        // The replaced line keeps the ending the old one had, mixed conventions and all.
        Assert.Equal("a\r\nB\nc\r\n", document.Text);
    }
}
