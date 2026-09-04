using EtAlii.Adp.Backend.Hierarchy;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.DependencyGraph.Tests;

/// <summary>
/// The document as bytes, which is where the round-trip claim either holds or does not.
/// </summary>
/// <remarks>
/// Every corpus file is round-tripped here, and the two cases sibling modules got wrong first -
/// appending at an unterminated end of file, and removing the last line - each have a test that
/// fails if its guard is removed. A round-trip test that only ever sees well-formed CRLF files
/// passes for the wrong reason, which is what the corpus exists to prevent.
/// </remarks>
public class DependencyGraphDocumentTests
{
    private static string FixturesFolder =>
        IoPath.Combine(AppContext.BaseDirectory, "Fixtures");

    public static TheoryData<string> EveryFixture()
    {
        var data = new TheoryData<string>();
        foreach (var path in Directory.EnumerateFiles(FixturesFolder, "*.dgr"))
        {
            data.Add(IoPath.GetFileName(path));
        }

        return data;
    }

    private static string Read(string fixture) =>
        File.ReadAllText(IoPath.Combine(FixturesFolder, fixture));

    [Fact]
    public void TheCorpusIsActuallyThere()
    {
        // Assert.
        // A theory over an empty folder passes silently, which would make every round-trip claim
        // below vacuous. This is the guard against a broken Content copy rather than broken code.
        Assert.True(
            Directory.EnumerateFiles(FixturesFolder, "*.dgr").Count() >= 11,
            $"the corpus should have been copied beside the test binary, in {FixturesFolder}");
    }

    [Theory]
    [MemberData(nameof(EveryFixture))]
    public void EveryFixture_RoundTripsByteForByte(string fixture)
    {
        // Arrange.
        var original = Read(fixture);

        // Act.
        var document = LineDocument.Parse(original);

        // Assert.
        Assert.Equal(original, document.Text);
    }

    [Theory]
    [MemberData(nameof(EveryFixture))]
    public void NoFixtureCarriesATime(string fixture)
    {
        // Arrange.
        var text = Read(fixture);

        // Assert.
        // The deletion, asserted rather than assumed. This type is the timeline with time removed,
        // so a begin, an end or a duration reaching the corpus is a fixture that was forked and
        // not finished - and it would be the parser's silent excuse to grow a date reader again.
        foreach (var key in new[] { "begin:", "end:", "duration:" })
        {
            Assert.DoesNotContain(key, text, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void TheCorpusReallyDoesCoverBothLineEndings()
    {
        // Arrange & act.
        var lf = Read("lf-line-endings.dgr");
        var crlf = Read("crlf-line-endings.dgr");

        // Assert.
        // If git ever normalises these two into the same file, every line-ending test below still
        // passes while testing nothing. This is the test that notices.
        Assert.DoesNotContain('\r', lf);
        Assert.Contains('\r', crlf);
    }

    [Fact]
    public void ADocumentWithNoTrailingNewline_KeepsNotHavingOne()
    {
        // Arrange & act.
        var original = Read("no-trailing-newline.dgr");
        var document = LineDocument.Parse(original);

        // Assert.
        Assert.False(original.EndsWith('\n'), "the fixture is meant to end without a newline");
        Assert.Equal("", document.Lines[^1].Ending);
        Assert.Equal(original, document.Text);
    }

    [Fact]
    public void AppendingToAnUnterminatedFile_DoesNotGiveItATrailingNewline()
    {
        // Arrange.
        // The guard under test: the previously-last line gains a terminator and the *new* last
        // line inherits the missing one, rather than the file simply gaining a newline.
        var document = LineDocument.Parse("first\r\nlast-without-newline");

        // Act.
        document.Insert(document.Lines.Count, ["appended"]);

        // Assert.
        Assert.Equal("first\r\nlast-without-newline\r\nappended", document.Text);
    }

    [Fact]
    public void AppendingThenRemoving_ComesBackByteForByte()
    {
        // Arrange.
        // The reason the guard exists at all: an edit and its inverse must cancel exactly, or
        // undo returns a file one byte different from the one the user had.
        const string original = "first\r\nlast-without-newline";
        var document = LineDocument.Parse(original);

        // Act.
        document.Insert(document.Lines.Count, ["appended"]);
        document.Remove(new LineRange(document.Lines.Count - 1, document.Lines.Count - 1));

        // Assert.
        Assert.Equal(original, document.Text);
    }

    [Fact]
    public void RemovingTheLastLine_PassesItsEndingToTheLineThatTakesItsPlace()
    {
        // Arrange.
        var document = LineDocument.Parse("alpha\r\nbeta\r\ngamma");

        // Act.
        document.Remove(new LineRange(2, 2));

        // Assert.
        // "gamma" had no terminator; "beta" inherits that, because how the file ends belongs to
        // the file rather than to whichever line is currently last.
        Assert.Equal("alpha\r\nbeta", document.Text);
    }

    [Fact]
    public void RemovingTheLastLineOfATerminatedFile_LeavesItTerminated()
    {
        // Arrange.
        var document = LineDocument.Parse("alpha\r\nbeta\r\ngamma\r\n");

        // Act.
        document.Remove(new LineRange(2, 2));

        // Assert.
        Assert.Equal("alpha\r\nbeta\r\n", document.Text);
    }

    [Fact]
    public void ReplacingAMiddleLine_TouchesNothingElse()
    {
        // Arrange.
        var document = LineDocument.Parse("alpha\r\nbeta\r\ngamma\r\n");

        // Act.
        document.Replace(new LineRange(1, 1), ["BETA"]);

        // Assert.
        Assert.Equal("alpha\r\nBETA\r\ngamma\r\n", document.Text);
    }

    [Fact]
    public void ReplacingTheUnterminatedLastLine_DoesNotTerminateIt()
    {
        // Arrange.
        var document = LineDocument.Parse("alpha\r\nomega");

        // Act.
        document.Replace(new LineRange(1, 1), ["OMEGA"]);

        // Assert.
        Assert.Equal("alpha\r\nOMEGA", document.Text);
    }

    [Fact]
    public void ReplacingOneLineWithSeveral_UsesTheDominantEndingForTheNewOnes()
    {
        // Arrange.
        var document = LineDocument.Parse("alpha\r\nbeta\r\n");

        // Act.
        document.Replace(new LineRange(1, 1), ["one", "two", "three"]);

        // Assert.
        Assert.Equal("alpha\r\none\r\ntwo\r\nthree\r\n", document.Text);
    }

    [Fact]
    public void AnLfDocument_StaysLfWhenEdited()
    {
        // Arrange.
        // The dominant ending is measured, not assumed: an edit must not smuggle the house style
        // into a file that consistently uses the other one.
        var document = LineDocument.Parse("alpha\nbeta\ngamma\n");

        // Act.
        document.Insert(1, ["inserted"]);

        // Assert.
        Assert.Equal("\n", document.DominantEnding);
        Assert.Equal("alpha\ninserted\nbeta\ngamma\n", document.Text);
    }

    [Fact]
    public void AMixedDocument_TakesTheMajorityEnding()
    {
        // Arrange & act.
        var document = LineDocument.Parse("a\r\nb\r\nc\nd\r\n");

        // Assert.
        Assert.Equal("\r\n", document.DominantEnding);
    }

    [Fact]
    public void AnEmptyDocument_RoundTripsAndDoesNotInventALine()
    {
        // Arrange & act.
        var document = LineDocument.Parse("");

        // Assert.
        Assert.Empty(document.Lines);
        Assert.Equal("", document.Text);
    }

    [Fact]
    public void ADocumentEndingInANewline_DoesNotGrowAPhantomLine()
    {
        // Arrange & act.
        // Parsing then re-parsing its own output is where an off-by-one shows up as a document
        // that gains a blank line every time it is opened.
        var once = LineDocument.Parse("alpha\r\n");
        var twice = LineDocument.Parse(once.Text);

        // Assert.
        Assert.Single(once.Lines);
        Assert.Single(twice.Lines);
    }

    [Fact]
    public void ARangeOutsideTheDocument_IsRefusedRatherThanClamped()
    {
        // Arrange.
        var document = LineDocument.Parse("alpha\r\n");

        // Act & assert.
        Assert.Throws<ArgumentOutOfRangeException>(() => document.Remove(new LineRange(0, 5)));
    }

    [Fact]
    public void AnInvertedRange_IsRefused()
    {
        // Arrange.
        var document = LineDocument.Parse("alpha\r\nbeta\r\n");

        // Act & assert.
        // Length would be zero or negative, and RemoveRange would either do nothing or throw
        // something less explanatory.
        Assert.Throws<ArgumentOutOfRangeException>(() => document.Remove(new LineRange(1, 0)));
    }

    [Fact]
    public void ALineRangeKnowsHowLongItIs()
    {
        // Assert.
        Assert.Equal(1, new LineRange(3, 3).Length);
        Assert.Equal(4, new LineRange(2, 5).Length);
    }
}
