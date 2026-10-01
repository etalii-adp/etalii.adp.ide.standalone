using System.Text;
using EtAlii.Adp.Specification.Fbl.Text;
using Xunit;

namespace EtAlii.Adp.Specification.Fbl.Tests.Bytes;

/// <summary>Bytes, lines and positions (FBL §2.6; Requirements 3.1 and 3.2).</summary>
public class BodyTextTests
{
    [Fact]
    public void AColumnCountsCodePointsNotBytesOrUtf16Units()
    {
        // Arrange: an emoji is four bytes and two UTF-16 units, but one code point.
        var text = new BodyText(Encoding.UTF8.GetBytes("a😀b: x\n"));

        // Act.
        var position = text.Position(Encoding.UTF8.GetByteCount("a😀b"));

        // Assert.
        Assert.Equal((1, 4), position);
    }

    [Fact]
    public void TheByteOrderMarkBelongsToNoColumn()
    {
        // Arrange.
        var text = new BodyText([0xEF, 0xBB, 0xBF, (byte)'a', (byte)'b']);

        // Act and assert.
        Assert.Equal(3, text.BomLength);
        Assert.Equal((1, 2), text.Position(4));
    }

    [Fact]
    public void CrlfLfAndALoneCrEachEndALine()
    {
        // Arrange.
        var text = new BodyText("a\r\nb\nc\rd"u8.ToArray());

        // Act and assert.
        Assert.Equal(["\r\n", "\n", "\r", ""], text.Lines.Select(l => l.Ending));
        Assert.Equal((4, 1), text.Position(7));
    }

    [Fact]
    public void ABodyOfOnlyALoneCrIsOneEmptyLineEndedByCr()
    {
        // Arrange.
        var text = new BodyText("\r"u8.ToArray());

        // Act and assert.
        var line = Assert.Single(text.Lines);
        Assert.Equal(new TextLine(0, 0, 1, "\r"), line);
        Assert.Equal("\r", text.DominantEnding);
    }

    [Theory]
    [InlineData("a\r\nb\nc", "\r\n")]
    [InlineData("a\r\nb\nc\n", "\n")]
    [InlineData("a\rb\rc\r\n", "\r\n")]
    [InlineData("abc", null)]
    public void CrlfWinsATieAndALoneCrCountsAsNeither(string body, string? dominant)
    {
        // Act and assert.
        Assert.Equal(dominant, new BodyText(Encoding.UTF8.GetBytes(body)).DominantEnding);
    }

    [Fact]
    public void AnInvalidUtf8SequenceIsFoundWithItsOffset()
    {
        // Arrange: a lone continuation byte after two valid bytes.
        var text = new BodyText([(byte)'a', (byte)'b', 0x80, (byte)'c']);

        // Act and assert.
        Assert.False(text.IsValidUtf8);
        Assert.Equal(2, text.InvalidOffset);
    }
}
