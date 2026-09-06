using EtAlii.Adp.Common;
using EtAlii.Adp.Hierarchy;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.Databricks.Tests;

/// <summary>
/// The line CST's one promise: a document nothing has spliced comes back byte-identical, whatever
/// its line endings, and edits touch only the lines they concern (databricks-diagrams
/// Requirement 2.1).
/// </summary>
public class DatabricksDocumentTests
{
    private static string Fixture(string name) =>
        IoPath.Combine(AppContext.BaseDirectory, "Fixtures", name);

    [Theory]
    [InlineData("bundle.yml")]
    [InlineData("job.yml")]
    [InlineData("pipeline.json")]
    [InlineData("crlf-line-endings.yml")]
    [InlineData("lf-line-endings.yml")]
    [InlineData("no-trailing-newline.yml")]
    [InlineData("broken.yml")]
    public void AnUntouchedDocument_RoundTrips_ByteIdentically(string name)
    {
        // Arrange.
        var text = File.ReadAllText(Fixture(name));

        // Act.
        var document = DatabricksDocument.Parse(text);

        // Assert.
        Assert.Equal(text, document.Text);
    }

    [Fact]
    public void RemoveThenInsert_AtAnUnterminatedEnd_ReturnsTheBytes()
    {
        // Arrange.
        // The reversibility trap a sibling module shipped: appending to a file with no trailing
        // newline must move the missing terminator to the new last line, so the append's own
        // undo comes back byte for byte.
        var text = File.ReadAllText(Fixture("no-trailing-newline.yml"));
        var document = DatabricksDocument.Parse(text);
        var lastLine = document.Lines[^1].Text;

        // Act.
        document.Insert(document.Lines.Count, ["  staging:", "    mode: development"]);
        var grown = document.Text;
        document.Remove(new LineRange(document.Lines.Count - 2, document.Lines.Count - 1));

        // Assert.
        Assert.EndsWith("    mode: development", grown, StringComparison.Ordinal);
        Assert.Equal(text, document.Text);
        Assert.Equal(lastLine, document.Lines[^1].Text);
    }

    [Fact]
    public void Replace_TouchesOnlyTheRangesLines()
    {
        // Arrange.
        var document = DatabricksDocument.Parse("a: 1\r\nb: 2\r\nc: 3\r\n");

        // Act.
        document.Replace(new LineRange(1, 1), ["b: 22"]);

        // Assert.
        Assert.Equal("a: 1\r\nb: 22\r\nc: 3\r\n", document.Text);
    }

    [Fact]
    public void DominantEnding_FollowsTheFile_AndTiesGoToTheHouseStyle()
    {
        // Arrange & act & assert.
        Assert.Equal("\n", DatabricksDocument.Parse("a: 1\nb: 2\n").DominantEnding);
        Assert.Equal("\r\n", DatabricksDocument.Parse("a: 1\r\nb: 2\r\n").DominantEnding);
        Assert.Equal("\r\n", DatabricksDocument.Parse("").DominantEnding);
    }
}
