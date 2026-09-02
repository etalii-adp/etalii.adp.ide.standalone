using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.DependencyGraph.Tests;

/// <summary>
/// Edits as splices: the lines an edit touches, and the many more it must not.
/// </summary>
/// <remarks>
/// Most of these assert on the whole document text rather than on the changed line, because
/// "only the affected lines changed" is the claim, and a test that inspects only the line it
/// expected to change cannot see the damage done elsewhere.
/// </remarks>
public class DependencyGraphWriterTests
{
    private static string FixturesFolder => IoPath.Combine(AppContext.BaseDirectory, "Fixtures");

    private static (DependencyGraphDocument Document, DependencyGraphModel Model) Load(string fixture)
    {
        var document = DependencyGraphDocument.Parse(File.ReadAllText(IoPath.Combine(FixturesFolder, fixture)));
        return (document, DependencyGraphParser.Parse(document));
    }

    private static (DependencyGraphDocument Document, DependencyGraphModel Model) From(string yaml)
    {
        var document = DependencyGraphDocument.Parse(yaml);
        return (document, DependencyGraphParser.Parse(document));
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
        var (document, model) = Load("simple.dgr");
        var before = document.Text;

        // Act.
        DependencyGraphWriter.SetLabel(document, model.Elements[0], "Renamed");

        // Assert.
        Assert.Equal(1, LinesChanged(before, document.Text));
        Assert.Contains("label: Renamed", document.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void MovingAnElement_ChangesOnlyItsCoordinateAndRow()
    {
        // Arrange.
        // A drag is the commonest edit this type has, and the one with the most to damage.
        var (document, model) = Load("simple.dgr");
        var before = document.Text;

        // Act.
        DependencyGraphWriter.SetX(document, model.Elements[0], 900);
        DependencyGraphWriter.SetRow(document, model.Elements[0], 4);

        // Assert.
        Assert.Equal(2, LinesChanged(before, document.Text));
        Assert.Contains("    x: 900", document.Text, StringComparison.Ordinal);
        Assert.Contains("    row: 4", document.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void AWholeCoordinate_IsWrittenWhole()
    {
        // Arrange.
        // 900 returning as 900.0 renders identically on a canvas, so nothing but the bytes
        // catches it - and it would turn every drag into a diff against the author's own form.
        var (document, model) = Load("coordinates.dgr");

        // Act.
        DependencyGraphWriter.SetX(document, model.Elements[0], 900d);

        // Assert.
        Assert.Contains("x: 900\r\n", document.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("x: 900.0", document.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void AFractionalCoordinate_IsWrittenInvariantly()
    {
        // Arrange.
        // A comma decimal separator would produce `x: 412,5`, which YAML reads as a string and
        // the parser then reads as zero - a node that silently jumps to the origin on one
        // machine and not another.
        var previous = Thread.CurrentThread.CurrentCulture;
        Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("nl-NL");
        try
        {
            var (document, model) = Load("simple.dgr");

            // Act.
            DependencyGraphWriter.SetX(document, model.Elements[0], 412.5d);

            // Assert.
            Assert.Contains("x: 412.5", document.Text, StringComparison.Ordinal);
            var reparsed = DependencyGraphParser.Parse(DependencyGraphDocument.Parse(document.Text));
            Assert.Equal(412.5d, reparsed.Elements[0].X);
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = previous;
        }
    }

    [Fact]
    public void EditingAnElement_LeavesTheCommentsAlone()
    {
        // Arrange.
        // The corpus file with a comment in every position. A splice that widened by one line
        // would eat one of them, and nothing else in the suite would notice.
        var (document, model) = Load("comments.dgr");
        var before = document.Text;

        // Act.
        DependencyGraphWriter.SetLabel(document, model.Elements[0], "Changed");

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
        var (document, model) = Load("unmodelled-keys.dgr");
        var before = document.Text;

        // Act.
        DependencyGraphWriter.SetRow(document, model.Elements[0], 9);

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
        var (document, model) = Load("indentation.dgr");

        // Act.
        DependencyGraphWriter.SetLabel(document, model.Elements[0], "Still deeply indented");

        // Assert.
        // The four-space item indent and the eight-space key indent both survive: a writer that
        // reformats to its own taste has broken the round-trip discipline even though the model
        // is right.
        Assert.Contains("        label: Still deeply indented", document.Text, StringComparison.Ordinal);
        Assert.Contains("    -   id: indent01", document.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void EditingAFileWithNoTrailingNewline_DoesNotGiveItOne()
    {
        // Arrange.
        var (document, model) = Load("no-trailing-newline.dgr");

        // Act.
        DependencyGraphWriter.SetLabel(document, model.Elements[0], "Edited");

        // Assert.
        Assert.False(document.Text.EndsWith('\n'));
    }

    [Fact]
    public void EditingAnLfDocument_KeepsItLf()
    {
        // Arrange.
        var (document, model) = Load("lf-line-endings.dgr");

        // Act.
        DependencyGraphWriter.SetRow(document, model.Elements[0], 4);

        // Assert.
        Assert.DoesNotContain('\r', document.Text);
    }

    [Fact]
    public void AnElementWithNoXKey_GainsOneWhereItBelongs()
    {
        // Arrange.
        // A hand-written node need not carry every key this module models, and moving it must
        // add the one it lacks rather than silently doing nothing.
        var (document, model) = From("dependencies: 1\r\nelements:\r\n  - id: a\r\n    label: No x yet\r\n    row: 0\r\n");

        // Act.
        DependencyGraphWriter.SetX(document, model.Elements[0], 260);

        // Assert.
        var reparsed = DependencyGraphParser.Parse(DependencyGraphDocument.Parse(document.Text));
        Assert.Equal(260d, reparsed.Elements[0].X);
        Assert.Contains("    x: 260", document.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void RemovingAnElement_TakesItsRelationsWithIt()
    {
        // Arrange.
        var (document, model) = Load("relations.dgr");
        var doomed = model.Elements.Single(element => element.Id == "src00001");

        // Act.
        var going = DependencyGraphWriter.RelationsTouching(model, doomed.Id);
        DependencyGraphWriter.RemoveElement(document, model, doomed);

        // Assert.
        // All three relations in that fixture touch the element, and the count is knowable
        // before the removal runs, which is what the confirmation needs.
        Assert.Equal(3, going.Count);
        var reparsed = DependencyGraphParser.Parse(DependencyGraphDocument.Parse(document.Text));
        Assert.Empty(reparsed.Relations);
        Assert.Single(reparsed.Elements);
        Assert.Equal("dst00001", reparsed.Elements[0].Id);
    }

    [Fact]
    public void RelationsTouching_CountsBothEnds()
    {
        // Arrange.
        // A dependency reaching a node counts as much as one leaving it: removing a node that
        // only ever appears as a `to` still orphans every edge that names it.
        var (_, model) = Load("relations.dgr");

        // Act & assert.
        Assert.Equal(3, DependencyGraphWriter.RelationsTouching(model, "dst00001").Count);
        Assert.Empty(DependencyGraphWriter.RelationsTouching(model, "nobody01"));
    }

    [Fact]
    public void RemovingAnElement_RemovesFromTheBottomUp()
    {
        // Arrange.
        // The ordering guard. Removing top-down would shift every later range, and the second
        // removal would then cut the wrong lines - which shows up as a mangled document rather
        // than as an exception, so it needs a test that reads the result.
        var (document, model) = Load("relations.dgr");
        var doomed = model.Elements.Single(element => element.Id == "dst00001");

        // Act.
        DependencyGraphWriter.RemoveElement(document, model, doomed);

        // Assert.
        var reparsed = DependencyGraphParser.Parse(DependencyGraphDocument.Parse(document.Text));
        Assert.Single(reparsed.Elements);
        Assert.Equal("src00001", reparsed.Elements[0].Id);
        Assert.Equal("Order service", reparsed.Elements[0].Label);
        Assert.Empty(reparsed.Relations);
    }

    [Fact]
    public void AddingAnElement_CopiesTheDocumentsOwnIndentation()
    {
        // Arrange.
        var (document, model) = Load("indentation.dgr");

        // Act.
        DependencyGraphWriter.InsertElement(document, model, "newone01", "Added", 740, 5);

        // Assert.
        Assert.Contains("    -   id: newone01", document.Text, StringComparison.Ordinal);
        var reparsed = DependencyGraphParser.Parse(DependencyGraphDocument.Parse(document.Text));
        Assert.Equal(3, reparsed.Elements.Count);
        Assert.Equal("Added", reparsed.Elements[2].Label);
        Assert.Equal(5, reparsed.Elements[2].Row);
        Assert.Equal(740d, reparsed.Elements[2].X);
    }

    [Fact]
    public void AddingARelation_WhenThereIsNoRelationsSectionYet_CreatesOne()
    {
        // Arrange.
        var (document, model) = From("dependencies: 1\r\nelements:\r\n  - id: a\r\n    x: 0\r\n  - id: b\r\n    x: 200\r\n");

        // Act.
        DependencyGraphWriter.InsertRelation(document, model, "conn0001", "a", "b", "depends on");

        // Assert.
        var reparsed = DependencyGraphParser.Parse(DependencyGraphDocument.Parse(document.Text));
        var relation = Assert.Single(reparsed.Relations);
        Assert.Equal("a", relation.From);
        Assert.Equal("b", relation.To);
        Assert.Equal("depends on", relation.Label);
        Assert.True(DependencyGraphWriter.HasRelationsSection(document));
    }

    [Fact]
    public void AddingARelationThenRemovingIt_TakesTheSectionItCreatedBackOut()
    {
        // Arrange.
        // The stray `relations:` header is the one line that would break undo's byte identity.
        const string original = "dependencies: 1\r\nelements:\r\n  - id: a\r\n    x: 0\r\n  - id: b\r\n    x: 200\r\n";
        var (document, model) = From(original);
        Assert.False(DependencyGraphWriter.HasRelationsSection(document));

        // Act.
        DependencyGraphWriter.InsertRelation(document, model, "conn0001", "a", "b", "");
        var reparsed = DependencyGraphParser.Parse(DependencyGraphDocument.Parse(document.Text));
        DependencyGraphWriter.RemoveRelation(document, reparsed.Relations[0]);
        DependencyGraphWriter.RemoveRelationsSectionIfEmpty(
            document, DependencyGraphParser.Parse(DependencyGraphDocument.Parse(document.Text)));

        // Assert.
        Assert.Equal(original, document.Text);
    }

    [Fact]
    public void AddingASecondRelationBetweenTheSamePair_IsFine()
    {
        // Arrange.
        var (document, model) = Load("relations.dgr");

        // Act.
        DependencyGraphWriter.InsertRelation(document, model, "conn0004", "src00001", "dst00001", "and again");

        // Assert.
        var reparsed = DependencyGraphParser.Parse(DependencyGraphDocument.Parse(document.Text));
        Assert.Equal(4, reparsed.Relations.Count);
    }

    [Fact]
    public void ARelationIsWrittenFromThenTo_SoTheDirectionIsReadableInTheDiff()
    {
        // Arrange.
        var (document, model) = Load("simple.dgr");

        // Act.
        DependencyGraphWriter.InsertRelation(document, model, "conn0009", "b3Rt9wYz", "k7Qv2mXa", "");

        // Assert.
        // `from` depends on `to`: which way round it went in is the type's entire meaning, so the
        // written form is asserted rather than only the reparsed model.
        Assert.Contains("    from: b3Rt9wYz\r\n    to: k7Qv2mXa", document.Text, StringComparison.Ordinal);
        var reparsed = DependencyGraphParser.Parse(DependencyGraphDocument.Parse(document.Text));
        var added = reparsed.Relations.Single(relation => relation.Id == "conn0009");
        Assert.Equal("b3Rt9wYz", added.From);
        Assert.Equal("k7Qv2mXa", added.To);
    }

    [Fact]
    public void RelabellingARelation_ChangesOneLine()
    {
        // Arrange.
        var (document, model) = Load("relations.dgr");
        var before = document.Text;

        // Act.
        DependencyGraphWriter.SetRelationLabel(document, model.Relations[0], "renamed");

        // Assert.
        Assert.Equal(1, LinesChanged(before, document.Text));
        Assert.Contains("label: renamed", document.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void ClearingARelationsLabel_RemovesTheKeyRatherThanLeavingItEmpty()
    {
        // Arrange.
        var (document, model) = Load("relations.dgr");

        // Act.
        DependencyGraphWriter.SetRelationLabel(document, model.Relations[0], "");

        // Assert.
        var reparsed = DependencyGraphParser.Parse(DependencyGraphDocument.Parse(document.Text));
        Assert.Equal("", reparsed.Relations[0].Label);
        Assert.DoesNotContain("label: authorises through", document.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void ALabelNeedingQuotes_GetsThem_AndAnOrdinaryOneDoesNot()
    {
        // Arrange.
        var (document, model) = Load("simple.dgr");

        // Act.
        DependencyGraphWriter.SetLabel(document, model.Elements[0], "Plain");
        var afterPlain = document.Text;
        var reparsedModel = DependencyGraphParser.Parse(DependencyGraphDocument.Parse(afterPlain));
        DependencyGraphWriter.SetLabel(document, reparsedModel.Elements[0], "Has: a colon");

        // Assert.
        // A document should not sprout quotation marks it never had, and must not lose meaning
        // when the value genuinely needs them.
        Assert.Contains("label: Plain", afterPlain, StringComparison.Ordinal);
        Assert.Contains("label: \"Has: a colon\"", document.Text, StringComparison.Ordinal);
        var final = DependencyGraphParser.Parse(DependencyGraphDocument.Parse(document.Text));
        Assert.Equal("Has: a colon", final.Elements[0].Label);
    }

    [Fact]
    public void EveryEditThenItsInverse_ComesBackByteForByte()
    {
        // Arrange.
        // The property that makes undo trustworthy: an edit and its opposite cancel exactly.
        var (document, model) = Load("simple.dgr");
        var original = document.Text;
        var originalLabel = model.Elements[0].Label;

        // Act.
        DependencyGraphWriter.SetLabel(document, model.Elements[0], "Temporarily different");
        var reparsed = DependencyGraphParser.Parse(DependencyGraphDocument.Parse(document.Text));
        DependencyGraphWriter.SetLabel(document, reparsed.Elements[0], originalLabel);

        // Assert.
        Assert.Equal(original, document.Text);
    }

    [Fact]
    public void AMoveThenItsInverse_ComesBackByteForByte()
    {
        // Arrange.
        var (document, model) = Load("coordinates.dgr");
        var original = document.Text;
        var element = model.Elements[1];
        var (wasX, wasRow) = (element.X, element.Row);

        // Act.
        DependencyGraphWriter.SetX(document, element, 1000);
        DependencyGraphWriter.SetRow(document, element, 9);
        var reparsed = DependencyGraphParser.Parse(DependencyGraphDocument.Parse(document.Text));
        DependencyGraphWriter.SetX(document, reparsed.Elements[1], wasX);
        DependencyGraphWriter.SetRow(document, reparsed.Elements[1], wasRow);

        // Assert.
        Assert.Equal(original, document.Text);
    }

    [Fact]
    public void AddingThenRemovingAnElement_ComesBackByteForByte()
    {
        // Arrange.
        var (document, model) = Load("simple.dgr");
        var original = document.Text;

        // Act.
        DependencyGraphWriter.InsertElement(document, model, "temp0001", "Temporary", 640, 7);
        var reparsed = DependencyGraphParser.Parse(DependencyGraphDocument.Parse(document.Text));
        var added = reparsed.Elements.Single(element => element.Id == "temp0001");
        DependencyGraphWriter.RemoveElement(document, reparsed, added);

        // Assert.
        Assert.Equal(original, document.Text);
    }

    [Fact]
    public void AddingThenRemovingAnElement_InAFileWithNoTrailingNewline_ComesBackByteForByte()
    {
        // Arrange.
        // The two hardest cases together: an append at an unterminated end of file, undone.
        var (document, model) = Load("no-trailing-newline.dgr");
        var original = document.Text;

        // Act.
        DependencyGraphWriter.InsertElement(document, model, "temp0002", "Temporary", 480, 3);
        var reparsed = DependencyGraphParser.Parse(DependencyGraphDocument.Parse(document.Text));
        var added = reparsed.Elements.Single(element => element.Id == "temp0002");
        DependencyGraphWriter.RemoveElement(document, reparsed, added);

        // Assert.
        Assert.Equal(original, document.Text);
        Assert.False(document.Text.EndsWith('\n'));
    }
}
