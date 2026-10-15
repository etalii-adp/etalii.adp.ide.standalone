using EtAlii.Adp.Documents;
using Xunit;

namespace EtAlii.Adp.Diagram.WardleyMap.Tests;

/// <summary>
/// The line endings this module writes since it moved onto the core line document, recorded
/// because the module's own line document chose differently in three places (code-structure
/// behaviour changes B-05 and B-15; core's behaviour wins by the ruling on B-05).
/// </summary>
public class WardleyLineEndingsTests
{
    [Fact]
    public void Append_ToAFileWithAsManyLfAsCrlf_EndsTheNewLineWithCrlf_AndLeavesTheLastLineAlone()
    {
        // Arrange: one CRLF and one LF, so neither ending is the more common.
        var document = LineDocument.Parse("title Tea\r\nanchor Business [0.95, 0.63]\n");

        // Act.
        WardleyWriter.Append(document, "component Cup [0.79, 0.61]");

        // Assert: the line the edit did not touch keeps its LF.
        Assert.Equal("title Tea\r\nanchor Business [0.95, 0.63]\ncomponent Cup [0.79, 0.61]\r\n", document.Text);
    }

    [Fact]
    public void Append_ToASingleUnterminatedLine_EndsThatLineWithCrlf_AndLeavesTheNewOneUnterminated()
    {
        // Arrange: no line ending at all, so nothing to learn one from.
        var document = LineDocument.Parse("title Tea");

        // Act.
        WardleyWriter.Append(document, "component Cup [0.79, 0.61]");

        // Assert.
        Assert.Equal("title Tea\r\ncomponent Cup [0.79, 0.61]", document.Text);
    }

    [Fact]
    public void RemoveLine_OfAnUnterminatedLastLine_LeavesTheNewLastLineUnterminated()
    {
        // Arrange.
        var document = LineDocument.Parse("title Tea\ncomponent Cup [0.79, 0.61]");

        // Act.
        WardleyWriter.RemoveLine(document, 2);

        // Assert: how the file ends belongs to the file, not to the line that was last.
        Assert.Equal("title Tea", document.Text);
    }
}
