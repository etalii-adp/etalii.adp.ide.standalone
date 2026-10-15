using Xunit;

namespace EtAlii.Adp.Documents.Tests;

/// <summary>
/// Offsets to lines, which the Turtle and SPARQL tokenizers report their findings with and the RDF
/// and SHACL writers splice by (code-structure T130, FR-043).
/// </summary>
public class LineIndexTests
{
    public static TheoryData<string> Texts() =>
    [
        "",
        "one line",
        "a\nb\nc\n",
        "a\r\nb\r\nc",
        "a\r\nb\nc\r\n",
        "\n\n\n",
        "@prefix ex: <http://example.org/> .\r\n\r\nex:a ex:b ex:c .",
    ];

    [Theory]
    [MemberData(nameof(Texts))]
    public void EveryOffset_IsOnTheLineThatHoldsIt(string text)
    {
        // Arrange.
        var index = new LineIndex(text);

        // Act and assert, offset by offset against a count of the line breaks before it.
        for (var offset = 0; offset <= text.Length; offset++)
        {
            var expected = text[..offset].Count(character => character == '\n');
            Assert.Equal(expected, index.LineOf(offset));
            Assert.True(index.StartOf(expected) <= offset);
        }
    }

    [Fact]
    public void ACrlfLinesCarriageReturn_BelongsToTheLineItEnds()
    {
        // Arrange.
        var index = new LineIndex("ab\r\ncd");

        // Act and assert.
        Assert.Equal(0, index.LineOf(2));
        Assert.Equal(0, index.LineOf(3));
        Assert.Equal(1, index.LineOf(4));
        Assert.Equal(4, index.StartOf(1));
    }

    [Fact]
    public void ATextEndingInALineBreak_HasAnEmptyLinePastItsLast()
    {
        // Arrange.
        // Where a parser reports what it found at the end of the file.
        var index = new LineIndex("a\nb\n");

        // Act and assert.
        Assert.Equal(3, index.Count);
        Assert.Equal(2, index.LineOf(4));
        Assert.Equal(2, index.LineOf(100));
    }

    [Theory]
    [MemberData(nameof(Texts))]
    public void ALineDocumentsIndex_IsTheIndexOfItsText(string text)
    {
        // Arrange.
        var document = LineDocument.Parse(text);

        // Act.
        var fromLines = document.Index();
        var fromText = new LineIndex(document.Text);

        // Assert.
        Assert.Equal(fromText.Count, fromLines.Count);
        for (var line = 0; line < fromText.Count; line++)
        {
            Assert.Equal(fromText.StartOf(line), fromLines.StartOf(line));
        }
    }
}
