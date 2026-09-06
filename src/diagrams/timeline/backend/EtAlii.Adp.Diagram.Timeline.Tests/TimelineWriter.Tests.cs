using EtAlii.Adp.Common;
using EtAlii.Adp.Hierarchy;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.Timeline.Tests;

/// <summary>
/// Edits as splices: the lines an edit touches, and the many more it must not.
/// </summary>
/// <remarks>
/// Most of these assert on the whole document text rather than on the changed line, because
/// "only the affected lines changed" is the claim, and a test that inspects only the line it
/// expected to change cannot see the damage done elsewhere.
/// </remarks>
public class TimelineWriterTests
{
    private static string FixturesFolder => IoPath.Combine(AppContext.BaseDirectory, "Fixtures");

    private static (LineDocument Document, TimelineModel Model) Load(string fixture)
    {
        var document = LineDocument.Parse(File.ReadAllText(IoPath.Combine(FixturesFolder, fixture)));
        return (document, TimelineParser.Parse(document));
    }

    private static (LineDocument Document, TimelineModel Model) From(string yaml)
    {
        var document = LineDocument.Parse(yaml);
        return (document, TimelineParser.Parse(document));
    }

    /// <summary>How many lines differ between two versions of a document.</summary>
    private static int LinesChanged(string before, string after)
    {
        var left = before.ReplaceLineEndings("\n").Split('\n');
        var right = after.ReplaceLineEndings("\n").Split('\n');
        var changed = Math.Abs(left.Length - right.Length);
        for (var i = 0; i < Math.Min(left.Length, right.Length); i++)
        {
            if (!string.Equals(left[i], right[i], StringComparison.Ordinal))
            {
                changed++;
            }
        }

        return changed;
    }

    [Fact]
    public void RenamingAnElement_ChangesExactlyOneLine()
    {
        // Arrange.
        var (document, model) = Load("simple.tml");
        var before = document.Text;

        // Act.
        TimelineWriter.SetLabel(document, model.Elements[0], "Renamed");

        // Assert.
        Assert.Equal(1, LinesChanged(before, document.Text));
        Assert.Contains("label: Renamed", document.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void EditingAnElement_LeavesTheCommentsAlone()
    {
        // Arrange.
        // The corpus file with a comment in every position. A splice that widened by one line
        // would eat one of them, and nothing else in the suite would notice.
        var (document, model) = Load("comments.tml");
        var before = document.Text;

        // Act.
        TimelineWriter.SetLabel(document, model.Elements[0], "Changed");

        // Assert.
        Assert.Equal(1, LinesChanged(before, document.Text));
        Assert.Contains("# A leading comment, before anything else.", document.Text, StringComparison.Ordinal);
        Assert.Contains("# Between two keys.", document.Text, StringComparison.Ordinal);
        Assert.Contains("# A trailing comment, after everything.", document.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void EditingAnElement_LeavesUnmodelledKeysAlone()
    {
        // Arrange.
        var (document, model) = Load("unmodelled-keys.tml");
        var before = document.Text;

        // Act.
        TimelineWriter.SetRow(document, model.Elements[0], 9);

        // Assert.
        Assert.Equal(1, LinesChanged(before, document.Text));
        Assert.Contains("colour: \"#3355ff\"", document.Text, StringComparison.Ordinal);
        Assert.Contains("owner: platform-team", document.Text, StringComparison.Ordinal);
        Assert.Contains("- first note", document.Text, StringComparison.Ordinal);
        Assert.Contains("revision: 7", document.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void EditingADocumentWithUnusualIndentation_DoesNotTidyIt()
    {
        // Arrange.
        var (document, model) = Load("indentation.tml");

        // Act.
        TimelineWriter.SetLabel(document, model.Elements[0], "Still deeply indented");

        // Assert.
        // The four-space item indent and the eight-space key indent both survive: a writer that
        // reformats to its own taste has broken Requirement 2.2 even though the model is right.
        Assert.Contains("        label: Still deeply indented", document.Text, StringComparison.Ordinal);
        Assert.Contains("    -   id: indent01", document.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void EditingAFileWithNoTrailingNewline_DoesNotGiveItOne()
    {
        // Arrange.
        var (document, model) = Load("no-trailing-newline.tml");

        // Act.
        TimelineWriter.SetLabel(document, model.Elements[0], "Edited");

        // Assert.
        Assert.False(document.Text.EndsWith('\n'));
    }

    [Fact]
    public void EditingAnLfDocument_KeepsItLf()
    {
        // Arrange.
        var (document, model) = Load("lf-line-endings.tml");

        // Act.
        TimelineWriter.SetRow(document, model.Elements[0], 4);

        // Assert.
        Assert.DoesNotContain('\r', document.Text);
    }

    [Fact]
    public void GivingAMomentAnEnd_AddsTheKeyWhereItBelongs()
    {
        // Arrange.
        var (document, model) = From("timeline: 1\r\nelements:\r\n  - id: a\r\n    label: Moment\r\n    begin: 2026-01-01\r\n    row: 0\r\n");

        // Act.
        TimelineWriter.SetEnd(document, model.Elements[0], "2026-01-05");

        // Assert.
        var reparsed = TimelineParser.Parse(LineDocument.Parse(document.Text));
        Assert.True(reparsed.Elements[0].IsPeriod);
        Assert.Equal("2026-01-05", reparsed.Elements[0].End!.Text);
        Assert.Contains("    end: 2026-01-05", document.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void RemovingAPeriodsEnd_MakesItAMoment()
    {
        // Arrange.
        var (document, model) = Load("simple.tml");

        // Act.
        TimelineWriter.SetEnd(document, model.Elements[0], null);

        // Assert.
        var reparsed = TimelineParser.Parse(LineDocument.Parse(document.Text));
        Assert.False(reparsed.Elements[0].IsPeriod);
    }

    [Fact]
    public void RemovingAnElement_TakesItsConnectionsWithIt()
    {
        // Arrange.
        var (document, model) = Load("connections.tml");
        var doomed = model.Elements.Single(element => element.Id == "src00001");

        // Act.
        var going = TimelineWriter.ConnectionsTouching(model, doomed.Id);
        TimelineWriter.RemoveElement(document, model, doomed);

        // Assert.
        // All three connections in that fixture touch the element, and the count is knowable
        // before the removal runs, which is what Requirement 2.5 asks for.
        Assert.Equal(3, going.Count);
        var reparsed = TimelineParser.Parse(LineDocument.Parse(document.Text));
        Assert.Empty(reparsed.Connections);
        Assert.Single(reparsed.Elements);
        Assert.Equal("dst00001", reparsed.Elements[0].Id);
    }

    [Fact]
    public void RemovingAnElement_RemovesFromTheBottomUp()
    {
        // Arrange.
        // The ordering guard. Removing top-down would shift every later range, and the second
        // removal would then cut the wrong lines - which shows up as a mangled document rather
        // than as an exception, so it needs a test that reads the result.
        var (document, model) = Load("connections.tml");
        var doomed = model.Elements.Single(element => element.Id == "dst00001");

        // Act.
        TimelineWriter.RemoveElement(document, model, doomed);

        // Assert.
        var reparsed = TimelineParser.Parse(LineDocument.Parse(document.Text));
        Assert.Single(reparsed.Elements);
        Assert.Equal("src00001", reparsed.Elements[0].Id);
        Assert.Equal("Source", reparsed.Elements[0].Label);
        Assert.Empty(reparsed.Connections);
    }

    [Fact]
    public void AddingAnElement_CopiesTheDocumentsOwnIndentation()
    {
        // Arrange.
        var (document, model) = Load("indentation.tml");

        // Act.
        TimelineWriter.InsertElement(document, model, "newone01", "Added", "2026-12-01", "2026-12-31", 5);

        // Assert.
        Assert.Contains("    -   id: newone01", document.Text, StringComparison.Ordinal);
        var reparsed = TimelineParser.Parse(LineDocument.Parse(document.Text));
        Assert.Equal(3, reparsed.Elements.Count);
        Assert.Equal("Added", reparsed.Elements[2].Label);
        Assert.Equal(5, reparsed.Elements[2].Row);
    }

    [Fact]
    public void AddingAMoment_WritesNoEndKey()
    {
        // Arrange.
        var (document, model) = Load("simple.tml");

        // Act.
        TimelineWriter.InsertElement(document, model, "moment99", "Just a moment", "2026-04-01", null, 2);

        // Assert.
        var reparsed = TimelineParser.Parse(LineDocument.Parse(document.Text));
        var added = reparsed.Elements.Single(element => element.Id == "moment99");
        Assert.False(added.IsPeriod);
    }

    [Fact]
    public void AddingAConnection_WhenThereIsNoConnectionsSectionYet_CreatesOne()
    {
        // Arrange.
        var (document, model) = From("timeline: 1\r\nelements:\r\n  - id: a\r\n    begin: 2026-01-01\r\n  - id: b\r\n    begin: 2026-02-01\r\n");

        // Act.
        TimelineWriter.InsertConnection(document, model, "conn0001", "a", "b", "leads to");

        // Assert.
        var reparsed = TimelineParser.Parse(LineDocument.Parse(document.Text));
        var connection = Assert.Single(reparsed.Connections);
        Assert.Equal("a", connection.From);
        Assert.Equal("b", connection.To);
        Assert.Equal("leads to", connection.Label);
    }

    [Fact]
    public void AddingASecondConnectionBetweenTheSamePair_IsFine()
    {
        // Arrange.
        var (document, model) = Load("connections.tml");

        // Act.
        TimelineWriter.InsertConnection(document, model, "conn0004", "src00001", "dst00001", "and again");

        // Assert.
        var reparsed = TimelineParser.Parse(LineDocument.Parse(document.Text));
        Assert.Equal(4, reparsed.Connections.Count);
    }

    [Fact]
    public void RelabellingAConnection_ChangesOneLine()
    {
        // Arrange.
        var (document, model) = Load("connections.tml");
        var before = document.Text;

        // Act.
        TimelineWriter.SetConnectionLabel(document, model.Connections[0], "renamed");

        // Assert.
        Assert.Equal(1, LinesChanged(before, document.Text));
        Assert.Contains("label: renamed", document.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void ClearingAConnectionsLabel_RemovesTheKeyRatherThanLeavingItEmpty()
    {
        // Arrange.
        var (document, model) = Load("connections.tml");

        // Act.
        TimelineWriter.SetConnectionLabel(document, model.Connections[0], "");

        // Assert.
        var reparsed = TimelineParser.Parse(LineDocument.Parse(document.Text));
        Assert.Equal("", reparsed.Connections[0].Label);
        Assert.DoesNotContain("label: hands over", document.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void ALabelNeedingQuotes_GetsThem_AndAnOrdinaryOneDoesNot()
    {
        // Arrange.
        var (document, model) = Load("simple.tml");

        // Act.
        TimelineWriter.SetLabel(document, model.Elements[0], "Plain");
        var afterPlain = document.Text;
        var reparsedModel = TimelineParser.Parse(LineDocument.Parse(afterPlain));
        TimelineWriter.SetLabel(document, reparsedModel.Elements[0], "Has: a colon");

        // Assert.
        // A document should not sprout quotation marks it never had, and must not lose meaning
        // when the value genuinely needs them.
        Assert.Contains("label: Plain", afterPlain, StringComparison.Ordinal);
        Assert.Contains("label: \"Has: a colon\"", document.Text, StringComparison.Ordinal);
        var final = TimelineParser.Parse(LineDocument.Parse(document.Text));
        Assert.Equal("Has: a colon", final.Elements[0].Label);
    }

    [Fact]
    public void EveryEditThenItsInverse_ComesBackByteForByte()
    {
        // Arrange.
        // The property that makes undo trustworthy: an edit and its opposite cancel exactly.
        var (document, model) = Load("simple.tml");
        var original = document.Text;
        var originalLabel = model.Elements[0].Label;

        // Act.
        TimelineWriter.SetLabel(document, model.Elements[0], "Temporarily different");
        var reparsed = TimelineParser.Parse(LineDocument.Parse(document.Text));
        TimelineWriter.SetLabel(document, reparsed.Elements[0], originalLabel);

        // Assert.
        Assert.Equal(original, document.Text);
    }

    [Fact]
    public void AddingThenRemovingAnElement_ComesBackByteForByte()
    {
        // Arrange.
        var (document, model) = Load("simple.tml");
        var original = document.Text;

        // Act.
        TimelineWriter.InsertElement(document, model, "temp0001", "Temporary", "2026-05-01", "2026-05-02", 7);
        var reparsed = TimelineParser.Parse(LineDocument.Parse(document.Text));
        var added = reparsed.Elements.Single(element => element.Id == "temp0001");
        TimelineWriter.RemoveElement(document, reparsed, added);

        // Assert.
        Assert.Equal(original, document.Text);
    }

    [Fact]
    public void AddingThenRemovingAnElement_InAFileWithNoTrailingNewline_ComesBackByteForByte()
    {
        // Arrange.
        // The two hardest cases together: an append at an unterminated end of file, undone.
        var (document, model) = Load("no-trailing-newline.tml");
        var original = document.Text;

        // Act.
        TimelineWriter.InsertElement(document, model, "temp0002", "Temporary", "2026-05-01", null, 3);
        var reparsed = TimelineParser.Parse(LineDocument.Parse(document.Text));
        var added = reparsed.Elements.Single(element => element.Id == "temp0002");
        TimelineWriter.RemoveElement(document, reparsed, added);

        // Assert.
        Assert.Equal(original, document.Text);
        Assert.False(document.Text.EndsWith('\n'));
    }
}
