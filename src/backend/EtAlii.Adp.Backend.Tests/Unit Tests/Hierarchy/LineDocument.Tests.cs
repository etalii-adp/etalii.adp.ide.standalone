using EtAlii.Adp.Common;
using EtAlii.Adp.Hierarchy;
using Xunit;

namespace EtAlii.Adp.Backend.Tests;

/// <summary>
/// The shared line-oriented document (file-io-centralization Requirement 3.1): splicing by line
/// range, with every line keeping its own terminator so a document nothing touched comes back
/// byte for byte.
/// </summary>
/// <remarks>
/// <para>
/// These cases are ported from the timeline module's own document tests rather than invented,
/// because they are the accumulated record of what this code got wrong on the way to being
/// right - the unterminated-append case in particular exists because a module shipped that bug.
/// Porting them keeps that record attached to the code it constrains, now that the code lives
/// in one place.
/// </para>
/// <para>
/// The modules keep their byte-identical fixture corpora, which are a different kind of test:
/// these prove the splice rules on small hand-written inputs, those prove real documents survive.
/// </para>
/// </remarks>
public class LineDocumentTests
{
    // ---- round tripping ---------------------------------------------------------------------

    [Fact]
    public void ADocumentNothingTouched_ComesBackByteForByte()
    {
        // Arrange & act.
        var document = LineDocument.Parse("alpha\r\nbeta\r\n");

        // Assert.
        Assert.Equal("alpha\r\nbeta\r\n", document.Text);
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
        // The failure this guards grows the document by one line per round trip.
        var document = LineDocument.Parse("only\r\n");

        // Assert.
        Assert.Single(document.Lines);
        Assert.Equal("only\r\n", document.Text);
    }

    [Fact]
    public void ADocumentWithNoTrailingNewline_KeepsNotHavingOne()
    {
        // Arrange & act.
        var document = LineDocument.Parse("first\r\nlast-without-newline");

        // Assert.
        Assert.Equal("", document.Lines[^1].Ending);
        Assert.Equal("first\r\nlast-without-newline", document.Text);
    }

    // ---- terminators ------------------------------------------------------------------------

    [Fact]
    public void AMixedDocument_TakesTheMajorityEnding()
    {
        // Arrange & act.
        var document = LineDocument.Parse("a\r\nb\r\nc\nd\r\n");

        // Assert.
        Assert.Equal("\r\n", document.DominantEnding);
    }

    [Fact]
    public void AnLfDocument_StaysLfWhenEdited()
    {
        // Arrange.
        var document = LineDocument.Parse("a\nb\nc\n");

        // Act.
        document.Insert(1, ["inserted"]);

        // Assert.
        Assert.Equal("a\ninserted\nb\nc\n", document.Text);
    }

    [Fact]
    public void AnEmptyDocumentsTie_GoesToTheHouseStyle()
    {
        // Arrange & act.
        // No evidence either way, and the repository writes CRLF - see .gitattributes.
        var document = LineDocument.Parse("");

        // Assert.
        Assert.Equal("\r\n", document.DominantEnding);
    }

    [Fact]
    public void EachLineKeepsItsOwnTerminator_RatherThanTheDocumentsMajority()
    {
        // Arrange & act.
        // A mixed document is not tidied up on the way through: both facts survive.
        var document = LineDocument.Parse("crlf\r\nlf\n");

        // Assert.
        Assert.Equal("\r\n", document.Lines[0].Ending);
        Assert.Equal("\n", document.Lines[1].Ending);
        Assert.Equal("crlf\r\nlf\n", document.Text);
    }

    // ---- replace ----------------------------------------------------------------------------

    [Fact]
    public void ReplacingAMiddleLine_TouchesNothingElse()
    {
        // Arrange.
        var document = LineDocument.Parse("a\r\nb\r\nc\r\n");

        // Act.
        document.Replace(new LineRange(1, 1), ["replaced"]);

        // Assert.
        Assert.Equal("a\r\nreplaced\r\nc\r\n", document.Text);
    }

    [Fact]
    public void ReplacingTheUnterminatedLastLine_DoesNotTerminateIt()
    {
        // Arrange.
        var document = LineDocument.Parse("a\r\nlast");

        // Act.
        document.Replace(new LineRange(1, 1), ["replaced"]);

        // Assert.
        Assert.Equal("a\r\nreplaced", document.Text);
    }

    [Fact]
    public void ReplacingOneLineWithSeveral_UsesTheDominantEndingForTheNewOnes()
    {
        // Arrange.
        var document = LineDocument.Parse("a\r\nb\r\n");

        // Act.
        document.Replace(new LineRange(0, 0), ["one", "two", "three"]);

        // Assert.
        Assert.Equal("one\r\ntwo\r\nthree\r\nb\r\n", document.Text);
    }

    [Fact]
    public void ReplacingAMultiLineRange_SwapsTheWholeSpan()
    {
        // Arrange.
        var document = LineDocument.Parse("a\r\nb\r\nc\r\nd\r\n");

        // Act.
        document.Replace(new LineRange(1, 2), ["only"]);

        // Assert.
        Assert.Equal("a\r\nonly\r\nd\r\n", document.Text);
    }

    // ---- insert -----------------------------------------------------------------------------

    [Fact]
    public void AppendingToAnUnterminatedFile_DoesNotGiveItATrailingNewline()
    {
        // Arrange.
        // The previously-last line gains a terminator and the NEW last line inherits the missing
        // one, rather than the file simply gaining a newline. A module shipped this bug first.
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
        // The reversibility this is really about: an append and its own undo must be a no-op.
        const string original = "first\r\nlast-without-newline";
        var document = LineDocument.Parse(original);

        // Act.
        document.Insert(document.Lines.Count, ["appended"]);
        document.Remove(new LineRange(document.Lines.Count - 1, document.Lines.Count - 1));

        // Assert.
        Assert.Equal(original, document.Text);
    }

    [Fact]
    public void InsertingAtTheStart_TouchesNoExistingLine()
    {
        // Arrange.
        var document = LineDocument.Parse("a\r\nb\r\n");

        // Act.
        document.Insert(0, ["zero"]);

        // Assert.
        Assert.Equal("zero\r\na\r\nb\r\n", document.Text);
    }

    // ---- remove -----------------------------------------------------------------------------

    [Fact]
    public void RemovingTheLastLine_PassesItsEndingToTheLineThatTakesItsPlace()
    {
        // Arrange.
        // How a file ends belongs to the file, not to whichever line happens to be last.
        var document = LineDocument.Parse("a\r\nb");

        // Act.
        document.Remove(new LineRange(1, 1));

        // Assert.
        Assert.Equal("a", document.Text);
    }

    [Fact]
    public void RemovingTheLastLineOfATerminatedFile_LeavesItTerminated()
    {
        // Arrange.
        var document = LineDocument.Parse("a\r\nb\r\n");

        // Act.
        document.Remove(new LineRange(1, 1));

        // Assert.
        Assert.Equal("a\r\n", document.Text);
    }

    [Fact]
    public void RemovingAMultiLineRange_TakesExactlyThatSpan()
    {
        // Arrange.
        var document = LineDocument.Parse("a\r\nb\r\nc\r\nd\r\n");

        // Act.
        document.Remove(new LineRange(1, 2));

        // Assert.
        Assert.Equal("a\r\nd\r\n", document.Text);
    }

    // ---- refusals ---------------------------------------------------------------------------

    [Fact]
    public void ARangeOutsideTheDocument_IsRefusedRatherThanClamped()
    {
        // Arrange.
        // Clamping would splice the wrong line, which is worse than an edit that does not happen.
        var document = LineDocument.Parse("alpha\r\n");

        // Act & assert.
        Assert.Throws<ArgumentOutOfRangeException>(() => document.Remove(new LineRange(0, 5)));
    }

    [Fact]
    public void AnInvertedRange_IsRefused()
    {
        // Arrange.
        var document = LineDocument.Parse("a\r\nb\r\n");

        // Act & assert.
        Assert.Throws<ArgumentOutOfRangeException>(() => document.Replace(new LineRange(1, 0), ["x"]));
    }

    [Fact]
    public void ANegativeStart_IsRefused()
    {
        // Arrange.
        var document = LineDocument.Parse("a\r\n");

        // Act & assert.
        Assert.Throws<ArgumentOutOfRangeException>(() => document.Remove(new LineRange(-1, 0)));
    }

    // ---- the line itself --------------------------------------------------------------------

    [Fact]
    public void ALineKnowsWhetherItIsBlankOrAComment()
    {
        // Arrange & act.
        var document = LineDocument.Parse("   \r\n# a comment\r\nreal\r\n");

        // Assert.
        Assert.True(document.Lines[0].IsBlank);
        Assert.True(document.Lines[1].IsComment);
        Assert.False(document.Lines[2].IsBlank);
        Assert.False(document.Lines[2].IsComment);
    }

    [Fact]
    public void ALineRangeKnowsHowLongItIs()
    {
        // Arrange, act & assert.
        Assert.Equal(1, new LineRange(3, 3).Length);
        Assert.Equal(3, new LineRange(2, 4).Length);
    }
}
