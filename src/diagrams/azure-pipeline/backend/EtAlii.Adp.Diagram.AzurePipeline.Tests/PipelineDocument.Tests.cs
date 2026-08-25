using System.Text;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.AzurePipeline.Tests;

/// <summary>
/// The round trip is this module's headline correctness property: a pipeline ADP did not change
/// comes back byte-identical (Requirement 3.1). These read the corpus from disk exactly as the
/// backend will, and compare bytes rather than strings - a comparison on strings would pass on a
/// file whose line endings had been rewritten, which is the failure most worth catching.
/// </summary>
public class PipelineDocumentTests
{
    private static string FixturePath(string name) => IoPath.Combine("Fixtures", name);

    public static TheoryData<string> AllFixtures()
    {
        var data = new TheoryData<string>();
        foreach (var path in Directory.GetFiles("Fixtures", "*.yml", SearchOption.AllDirectories))
        {
            data.Add(IoPath.GetRelativePath("Fixtures", path));
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(AllFixtures))]
    public void EveryFixture_RoundTripsByteForByte(string relativePath)
    {
        // Arrange.
        var path = FixturePath(relativePath);
        var original = File.ReadAllBytes(path);

        // Act.
        var document = PipelineDocument.Parse(File.ReadAllText(path));
        var written = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(document.Text);

        // Assert.
        Assert.Equal(original, written);
    }

    [Fact]
    public void ACrlfFixture_KeepsItsCarriageReturns()
    {
        // Arrange: the pair of CRLF and LF fixtures exists to prove the writer preserves what it
        // read rather than imposing a house style, so this asserts the premise still holds.
        var text = File.ReadAllText(FixturePath("edge-crlf.yml"));

        // Act.
        var document = PipelineDocument.Parse(text);

        // Assert.
        Assert.All(document.Lines.Take(document.Lines.Count - 1), line => Assert.Equal("\r\n", line.Ending));
        Assert.Equal("\r\n", document.DominantEnding);
    }

    [Fact]
    public void AFileWithoutATrailingNewline_DoesNotGainOne()
    {
        // Arrange.
        const string text = "trigger:\n  - main";

        // Act.
        var document = PipelineDocument.Parse(text);

        // Assert.
        Assert.Equal(text, document.Text);
        Assert.Equal("", document.Lines[^1].Ending);
    }

    [Fact]
    public void AFileWithATrailingNewline_DoesNotGainAPhantomLine()
    {
        // Arrange: the mistake that grows a document by one line per round trip.
        const string text = "trigger:\n  - main\n";

        // Act.
        var document = PipelineDocument.Parse(text);

        // Assert.
        Assert.Equal(2, document.Lines.Count);
        Assert.Equal(text, document.Text);
    }

    [Fact]
    public void AMixedEndingFile_KeepsEachLineAsItWas()
    {
        // Arrange: a document edited on two machines. Rewriting one line must not convert others.
        const string text = "a: 1\r\nb: 2\nc: 3\r\n";

        // Act.
        var document = PipelineDocument.Parse(text);

        // Assert.
        Assert.Equal(text, document.Text);
        Assert.Equal(["\r\n", "\n", "\r\n"], document.Lines.Select(line => line.Ending));
    }

    [Fact]
    public void Replace_ChangesItsOwnLinesAndNothingElse()
    {
        // Arrange.
        var document = PipelineDocument.Parse("a: 1\nb: 2\nc: 3\n");

        // Act.
        document.Replace(PipelineLineRange.Single(1), ["b: replaced"]);

        // Assert.
        Assert.Equal("a: 1\nb: replaced\nc: 3\n", document.Text);
    }

    [Fact]
    public void Replace_OverSeveralLines_KeepsTheSurroundingOnes()
    {
        // Arrange.
        var document = PipelineDocument.Parse("keep\nold one\nold two\nkeep too\n");

        // Act.
        document.Replace(new PipelineLineRange(1, 2), ["new"]);

        // Assert.
        Assert.Equal("keep\nnew\nkeep too\n", document.Text);
    }

    [Fact]
    public void Replace_OfTheLastLine_OfAFileWithoutATrailingNewline_StillHasNone()
    {
        // Arrange: the splice must inherit the ending the range had, not the document's.
        var document = PipelineDocument.Parse("a: 1\nb: 2");

        // Act.
        document.Replace(PipelineLineRange.Single(1), ["b: replaced"]);

        // Assert.
        Assert.Equal("a: 1\nb: replaced", document.Text);
    }

    [Fact]
    public void Insert_AtTheEndOfAFileWithoutATrailingNewline_DoesNotRunTheLinesTogether()
    {
        // Arrange.
        var document = PipelineDocument.Parse("a: 1\nb: 2");

        // Act.
        document.Insert(2, ["c: 3"]);

        // Assert.
        Assert.Equal("a: 1\nb: 2\nc: 3\n", document.Text);
    }

    [Fact]
    public void Insert_AdoptsTheDocumentsOwnEnding()
    {
        // Arrange: an edit must not introduce a second convention into a consistent file.
        var document = PipelineDocument.Parse("a: 1\r\nb: 2\r\n");

        // Act.
        document.Insert(1, ["inserted"]);

        // Assert.
        Assert.Equal("a: 1\r\ninserted\r\nb: 2\r\n", document.Text);
    }

    [Fact]
    public void Remove_TakesItsLinesAndLeavesTheRest()
    {
        // Arrange.
        var document = PipelineDocument.Parse("a: 1\nb: 2\nc: 3\n");

        // Act.
        document.Remove(PipelineLineRange.Single(1));

        // Assert.
        Assert.Equal("a: 1\nc: 3\n", document.Text);
    }

    [Fact]
    public void ASpliceIntoAFixture_LeavesEveryOtherByteAlone()
    {
        // Arrange: the property Requirement 3.2 actually asks for, checked on a real document
        // rather than a toy - a comment-heavy one, since comments are what a serialiser loses.
        var text = File.ReadAllText(FixturePath("edge-comments.yml"));
        var document = PipelineDocument.Parse(text);
        var target = document.Lines
            .Select((line, index) => (line, index))
            .First(pair => pair.line.Text.Contains("displayName: Build", StringComparison.Ordinal))
            .index;
        var before = document.Lines.Select(line => line.ToString()).ToArray();

        // Act.
        document.Replace(PipelineLineRange.Single(target), ["          displayName: Renamed   # trailing again"]);

        // Assert.
        var after = document.Lines.Select(line => line.ToString()).ToArray();
        Assert.Equal(before.Length, after.Length);
        for (var i = 0; i < before.Length; i++)
        {
            if (i == target)
            {
                Assert.NotEqual(before[i], after[i]);
            }
            else
            {
                Assert.Equal(before[i], after[i]);
            }
        }
    }

    [Fact]
    public void Replace_OutsideTheDocument_Throws()
    {
        // Arrange: an off-by-one in a splice rewrites somebody's pipeline, so it is a
        // programming error rather than something to absorb.
        var document = PipelineDocument.Parse("a: 1\n");

        // Act & assert.
        Assert.Throws<ArgumentOutOfRangeException>(() => document.Replace(PipelineLineRange.Single(5), ["x"]));
    }
}
