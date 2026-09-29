using System.Text;
using EtAlii.Adp.Documents;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.AzureDevOpsPipeline.Tests;

/// <summary>
/// The round trip is this module's headline correctness property: a pipeline ADP did not change
/// comes back byte-identical (Requirement 3.1). These read the corpus from disk exactly as the
/// backend will, and compare bytes rather than strings - a comparison on strings would pass on a
/// file whose line endings had been rewritten, which is the failure most worth catching. The
/// document is core's <see cref="LineDocument"/>, which replaced this module's own copy
/// (backend-centralization Requirement 1.1); these stay here because the corpus is this module's.
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
        var document = LineDocument.Parse(File.ReadAllText(path));
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
        var document = LineDocument.Parse(text);

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
        var document = LineDocument.Parse(text);

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
        var document = LineDocument.Parse(text);

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
        var document = LineDocument.Parse(text);

        // Assert.
        Assert.Equal(text, document.Text);
        Assert.Equal(["\r\n", "\n", "\r\n"], document.Lines.Select(line => line.Ending));
    }

    [Fact]
    public void Replace_ChangesItsOwnLinesAndNothingElse()
    {
        // Arrange.
        var document = LineDocument.Parse("a: 1\nb: 2\nc: 3\n");

        // Act.
        document.Replace(new LineRange(1, 1), ["b: replaced"]);

        // Assert.
        Assert.Equal("a: 1\nb: replaced\nc: 3\n", document.Text);
    }

    [Fact]
    public void Replace_OverSeveralLines_KeepsTheSurroundingOnes()
    {
        // Arrange.
        var document = LineDocument.Parse("keep\nold one\nold two\nkeep too\n");

        // Act.
        document.Replace(new LineRange(1, 2), ["new"]);

        // Assert.
        Assert.Equal("keep\nnew\nkeep too\n", document.Text);
    }

    [Fact]
    public void Replace_OfTheLastLine_OfAFileWithoutATrailingNewline_StillHasNone()
    {
        // Arrange: the splice must inherit the ending the range had, not the document's.
        var document = LineDocument.Parse("a: 1\nb: 2");

        // Act.
        document.Replace(new LineRange(1, 1), ["b: replaced"]);

        // Assert.
        Assert.Equal("a: 1\nb: replaced", document.Text);
    }

    [Fact]
    public void Insert_AtTheEndOfAFileWithoutATrailingNewline_DoesNotRunTheLinesTogether()
    {
        // Arrange.
        var document = LineDocument.Parse("a: 1\nb: 2");

        // Act.
        document.Insert(2, ["c: 3"]);

        // Assert.
        // The old last line gains a terminator and the new one inherits the missing one, so the
        // file still ends the way it did - which is what makes the append reversible.
        Assert.Equal("a: 1\nb: 2\nc: 3", document.Text);
    }

    [Fact]
    public void AppendingThenRemoving_LeavesAnUnterminatedFileExactlyAsItWas()
    {
        // Arrange: an undo has to return the file byte for byte, and a trailing newline quietly
        // acquired on the way is exactly the kind of one-byte difference a reviewer notices and
        // nobody can explain.
        const string original = "a: 1\nb: 2";
        var document = LineDocument.Parse(original);

        // Act.
        document.Insert(2, ["c: 3", "d: 4"]);
        document.Remove(new LineRange(2, 3));

        // Assert.
        Assert.Equal(original, document.Text);
    }

    [Fact]
    public void AppendingToATerminatedFile_KeepsItTerminated()
    {
        // Arrange: the other half of the same rule - a file that ends in a newline goes on doing so.
        var document = LineDocument.Parse("a: 1\nb: 2\n");

        // Act.
        document.Insert(2, ["c: 3"]);

        // Assert.
        Assert.Equal("a: 1\nb: 2\nc: 3\n", document.Text);
    }

    [Fact]
    public void Insert_AdoptsTheDocumentsOwnEnding()
    {
        // Arrange: an edit must not introduce a second convention into a consistent file.
        var document = LineDocument.Parse("a: 1\r\nb: 2\r\n");

        // Act.
        document.Insert(1, ["inserted"]);

        // Assert.
        Assert.Equal("a: 1\r\ninserted\r\nb: 2\r\n", document.Text);
    }

    [Fact]
    public void Remove_TakesItsLinesAndLeavesTheRest()
    {
        // Arrange.
        var document = LineDocument.Parse("a: 1\nb: 2\nc: 3\n");

        // Act.
        document.Remove(new LineRange(1, 1));

        // Assert.
        Assert.Equal("a: 1\nc: 3\n", document.Text);
    }

    [Fact]
    public void ASpliceIntoAFixture_LeavesEveryOtherByteAlone()
    {
        // Arrange: the property Requirement 3.2 actually asks for, checked on a real document
        // rather than a toy - a comment-heavy one, since comments are what a serialiser loses.
        var text = File.ReadAllText(FixturePath("edge-comments.yml"));
        var document = LineDocument.Parse(text);
        var target = document.Lines
            .Select((line, index) => (line, index))
            .First(pair => pair.line.Text.Contains("displayName: Build", StringComparison.Ordinal))
            .index;
        var before = document.Lines.Select(line => line.Text + line.Ending).ToArray();

        // Act.
        document.Replace(new LineRange(target, target), ["          displayName: Renamed   # trailing again"]);

        // Assert.
        var after = document.Lines.Select(line => line.Text + line.Ending).ToArray();
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
        var document = LineDocument.Parse("a: 1\n");

        // Act & assert.
        Assert.Throws<ArgumentOutOfRangeException>(() => document.Replace(new LineRange(5, 5), ["x"]));
    }

    [Fact]
    public void ATiedFile_GivesAnInsertedLineCrlf()
    {
        // Arrange: as many LF endings as CRLF, so neither is the majority and the tie rule alone
        // decides. This module's own copy gave LF here; core gives CRLF, the house style, and
        // core's rule is the one that stands (backend-centralization Requirement 1.2).
        var text = File.ReadAllText(FixturePath("edge-tied-endings.yml"));
        var document = LineDocument.Parse(text);
        var endings = document.Lines.Select(line => line.Ending).ToList();
        Assert.Equal(endings.Count(ending => ending == "\n"), endings.Count(ending => ending == "\r\n"));

        // Act.
        document.Insert(1, ["  - release/*"]);

        // Assert.
        Assert.Equal("\r\n", document.Lines[1].Ending);
    }

    [Fact]
    public void ARangeEndingTheLineBeforeItStarts_IsRefused()
    {
        // Arrange: such a range has a length of zero, so without a refusal of its own a replace
        // splices its lines in as an insertion and a remove quietly does nothing. This module's
        // own copy did both; core refuses, and core's rule is the one that stands
        // (backend-centralization Requirement 1.4).
        const string text = "a: 1\nb: 2\nc: 3\n";
        var document = LineDocument.Parse(text);

        // Act & assert.
        Assert.Throws<ArgumentOutOfRangeException>(() => document.Replace(new LineRange(2, 1), ["x: 9"]));
        Assert.Throws<ArgumentOutOfRangeException>(() => document.Remove(new LineRange(2, 1)));
        Assert.Equal(text, document.Text);
    }
}
