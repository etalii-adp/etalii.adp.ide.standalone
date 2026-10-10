using EtAlii.Adp.Designer.TableModel;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Designer.Knowledge.Tests;

/// <summary>
/// What a knowledge file is told about itself (knowledge-designer Requirements 8.2 and 8.5): one
/// finding code per condition, each from a file that has the condition, at the place it is about -
/// and nothing that is reported is removed or changed by reporting it.
/// </summary>
public sealed class KnowledgeFindingsTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("adp-knowledge-findings-").FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A watcher may still be letting go; the temp folder is the system's to clear.
        }
    }

    /// <summary>A small table with nothing wrong in it: a title, a number, a selection, one view, two rows.</summary>
    private static readonly string Sound = """
        ded: "0.1"
        designer: etalii/knowledge
        name: Cities
        properties:
          - id: p1
            name: Name
            type: text
            title: true
          - id: p2
            name: People
            type: number
          - id: p3
            name: Size
            type: selection
            options:
              - id: o1
                name: Large
        views:
          - id: v1
            name: All
        rows:
          - id: r1
            cells:
              - property: p1
                text: Amsterdam
              - property: p2
                number: 931298
              - property: p3
                option: o1
          - id: r2
            cells:
              - property: p1
                text: Antwerp

        """.ReplaceLineEndings("\n");

    private string Write(string content, string name = "table.yaml")
    {
        var path = IoPath.Combine(_root, name);
        File.WriteAllText(path, content.ReplaceLineEndings("\r\n"));
        return path;
    }

    private static string Changed(string old, string @new)
    {
        Assert.Contains(old, Sound, StringComparison.Ordinal);
        return Sound.Replace(old, @new, StringComparison.Ordinal);
    }

    private static IReadOnlyList<TableFinding> FindingsOf(string path)
    {
        var session = new KnowledgeSession(path);
        try
        {
            return session.Baseline().Findings;
        }
        finally
        {
            session.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    [Fact]
    public void ASoundFile_IsToldNothing()
    {
        // Assert: the control every case below is a change of.
        Assert.Empty(FindingsOf(Write(Sound)));
    }

    /// <summary>Each condition as a change of the sound file, the code it is reported under, how serious it is, and where.</summary>
    public static TheoryData<string, string, string, string, string, string> Conditions() => new()
    {
        { KnowledgeValidator.NoTitle, "Warning", "", "", "    title: true\n", "" },
        { KnowledgeValidator.SeveralTitles, "Warning", "", "p2", "    name: People\n    type: number\n", "    name: People\n    type: text\n    title: true\n" },
        { KnowledgeValidator.TitleNotText, "Error", "", "p1", "    name: Name\n    type: text\n", "    name: Name\n    type: number\n" },
        { KnowledgeValidator.DuplicatePropertyName, "Error", "", "p2", "    name: People\n", "    name: name\n" },
        { KnowledgeValidator.UnknownProperty, "Warning", "r2", "p9", "      - property: p1\n        text: Antwerp\n", "      - property: p1\n        text: Antwerp\n      - property: p9\n        text: stray\n" },
        { KnowledgeValidator.ValueOfAnotherType, "Warning", "r2", "p2", "        text: Antwerp\n", "        text: Antwerp\n      - property: p2\n        text: a great many\n" },
        { KnowledgeValidator.UnknownOption, "Warning", "r1", "p3", "        option: o1\n", "        option: o9\n" },
        { KnowledgeValidator.ViewNamesMissingProperty, "Warning", "", "", "    name: All\n", "    name: All\n    groupBy: p9\n" },
        { KnowledgeValidator.ViewNamesMissingProperty, "Warning", "", "", "    name: All\n", "    name: All\n    sorts:\n      - property: p9\n" },
        { KnowledgeValidator.UnknownType, "Warning", "", "p2", "    name: People\n    type: number\n", "    name: People\n    type: person\n" },
        { KnowledgeValidator.MissingId, "Info", "row@30", "", "  - id: r2\n    cells:\n", "  - cells:\n" },
    };

    [Theory]
    [MemberData(nameof(Conditions))]
    public void ACondition_IsReportedUnderItsCode_AtItsPlace(string code, string severity, string rowId, string columnId, string old, string @new)
    {
        // Arrange.
        var path = Write(Changed(old, @new));

        // Act.
        var findings = FindingsOf(path);

        // Assert.
        var finding = Assert.Single(findings, candidate => candidate.Code == code);
        Assert.Equal(severity, finding.Severity.ToString());
        Assert.Equal((rowId, columnId), (finding.RowId, finding.ColumnId));
        Assert.NotEqual("", finding.Message);
    }

    [Fact]
    public async Task AKeyTheDesignerDoesNotKnow_IsReported_WhereverItStands_AndKept()
    {
        // Arrange: a key of the file, of a property, of an option, of a view, of a row and of a cell that no rule reads.
        var path = Write(Sound
            .Replace("name: Cities\n", "name: Cities\nowner: Peter\n", StringComparison.Ordinal)
            .Replace("    name: People\n", "    name: People\n    unit: people\n", StringComparison.Ordinal)
            .Replace("        name: Large\n", "        name: Large\n        icon: star\n", StringComparison.Ordinal)
            .Replace("    name: All\n", "    name: All\n    density: compact\n", StringComparison.Ordinal)
            .Replace("  - id: r2\n", "  - id: r2\n    weight: 3\n", StringComparison.Ordinal)
            .Replace("        text: Antwerp\n", "        text: Antwerp\n        note: a port\n", StringComparison.Ordinal));

        // Act.
        var unknown = FindingsOf(path).Where(finding => finding.Code == KnowledgeValidator.UnknownKey).ToList();

        // Assert: each is said once, as information, by its name and its line.
        string[] keys = ["owner", "unit", "icon", "density", "weight", "note"];
        Assert.Equal(keys.Length, unknown.Count);
        Assert.All(keys, key => Assert.Single(unknown, finding => finding.Message.Contains($"'{key}'", StringComparison.Ordinal) && finding.Message.Contains("(line ", StringComparison.Ordinal)));
        Assert.All(unknown, finding => Assert.Equal(TableFindingSeverity.Info, finding.Severity));

        // Act: an edit beside them.
        var before = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);
        await using var table = new EditingTable(path);
        await table.Edit(new TableGesture("setCell", RowId: "r2", ColumnId: "p1", Values: ["Antwerpen"]));

        // Assert: one line changed, and every key nobody reads is where it was.
        Assert.Equal(before.Replace("text: Antwerp\r\n", "text: Antwerpen\r\n", StringComparison.Ordinal), await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void AKeyTheDesignerDoesNotKnow_IsReported_InAJsonFileToo()
    {
        // Arrange: the shipped example, with a key of the file and a key of its first property that no rule reads.
        var path = KnowledgeFiles.CopyExample("cities.json", _root);
        var text = File.ReadAllText(path);
        Assert.Empty(FindingsOf(path).Where(finding => finding.Code == KnowledgeValidator.UnknownKey));
        File.WriteAllText(path, text
            .Replace("\"name\": \"Cities\",", "\"name\": \"Cities\", \"owner\": \"Peter\",", StringComparison.Ordinal)
            .Replace("{ \"id\": \"p1\",", "{ \"id\": \"p1\", \"unit\": \"none\",", StringComparison.Ordinal));

        // Act.
        var unknown = FindingsOf(path).Where(finding => finding.Code == KnowledgeValidator.UnknownKey).ToList();

        // Assert.
        Assert.Equal(2, unknown.Count);
        Assert.Single(unknown, finding => finding.Message.Contains("'owner'", StringComparison.Ordinal));
        Assert.Single(unknown, finding => finding.Message.Contains("'unit'", StringComparison.Ordinal));
    }

    [Fact]
    public void AnAttributeOrElementTheDesignerDoesNotKnow_IsReported_InAnXmlFile()
    {
        // Arrange: the shipped example, with an attribute of the file, an attribute of its first property and an element of the file that no rule reads.
        var path = KnowledgeFiles.CopyExample("cities.xml", _root);
        var text = File.ReadAllText(path);
        Assert.Empty(FindingsOf(path).Where(finding => finding.Code == KnowledgeValidator.UnknownKey));
        File.WriteAllText(path, text
            .Replace(" name=\"Cities\"", " name=\"Cities\" owner=\"Peter\"", StringComparison.Ordinal)
            .Replace("<property id=\"p1\"", "<property id=\"p1\" unit=\"none\"", StringComparison.Ordinal)
            .Replace("  <properties>", "  <notes>Kept by hand.</notes>\r\n  <properties>", StringComparison.Ordinal));

        // Act.
        var unknown = FindingsOf(path).Where(finding => finding.Code == KnowledgeValidator.UnknownKey).ToList();

        // Assert.
        Assert.Equal(3, unknown.Count);
        Assert.Single(unknown, finding => finding.Message.Contains("attribute 'owner'", StringComparison.Ordinal));
        Assert.Single(unknown, finding => finding.Message.Contains("attribute 'unit'", StringComparison.Ordinal));
        Assert.Single(unknown, finding => finding.Message.Contains("element 'notes'", StringComparison.Ordinal));
    }

    [Fact]
    public void ATableWithoutAView_IsShownAsIfItHadOne_AndSaysSo()
    {
        // Arrange.
        var path = Write(Changed("views:\n  - id: v1\n    name: All\n", ""));

        // Act.
        var findings = FindingsOf(path);

        // Assert.
        Assert.Contains(findings, finding => finding.Code == KnowledgeValidator.NoView);
    }

    [Fact]
    public void ARelationToOneRow_HoldingSeveral_IsReported_AndKeepsThem()
    {
        // Arrange.
        var path = Write(Changed(
            "        text: Antwerp\n",
            "        text: Antwerp\n      - property: p4\n        rows:\n          - row: r1\n          - row: r2\n")
            .Replace("views:\n", "  - id: p4\n    name: Near\n    type: relation\n    target: .\n    limit: one\nviews:\n", StringComparison.Ordinal));

        // Act.
        var findings = FindingsOf(path);

        // Assert.
        var finding = Assert.Single(findings, candidate => candidate.Code == KnowledgeValidator.RelationOverLimit);
        Assert.Equal(("r2", "p4"), (finding.RowId, finding.ColumnId));
        Assert.Equal(["r1", "r2"], KnowledgeDocumentStore.Read(path).Body!.Table.Rows.Single(row => row.Id == "r2").Cells.Single(cell => cell.PropertyId == "p4").Values);
    }

    [Fact]
    public void RowsThatAreEachOthersParents_AreEachReported()
    {
        // Arrange: r1 under r2 and r2 under r1.
        var path = Write(Sound
            .Replace("views:\n", "  - id: p4\n    name: Under\n    type: relation\n    target: .\n    limit: one\n    parent: true\nviews:\n", StringComparison.Ordinal)
            .Replace("        option: o1\n", "        option: o1\n      - property: p4\n        rows:\n          - row: r2\n", StringComparison.Ordinal)
            .Replace("        text: Antwerp\n", "        text: Antwerp\n      - property: p4\n        rows:\n          - row: r1\n", StringComparison.Ordinal));

        // Act.
        var cycles = FindingsOf(path).Where(finding => finding.Code == KnowledgeValidator.ParentCycle).ToList();

        // Assert.
        Assert.Equal(["r1", "r2"], cycles.Select(finding => finding.RowId).Order(StringComparer.Ordinal));
        Assert.All(cycles, finding => Assert.Equal(TableFindingSeverity.Error, finding.Severity));
    }

    [Fact]
    public void AFileWithSeveralThingsWrong_IsToldAllOfThem()
    {
        // Arrange: no title, an option that is not there, and a view grouped by a property that is not there.
        var path = Write(Sound
            .Replace("    title: true\n", "", StringComparison.Ordinal)
            .Replace("        option: o1\n", "        option: o9\n", StringComparison.Ordinal)
            .Replace("    name: All\n", "    name: All\n    groupBy: p9\n", StringComparison.Ordinal));

        // Act.
        var codes = FindingsOf(path).Select(finding => finding.Code).ToHashSet();

        // Assert.
        Assert.Superset(new HashSet<string> { KnowledgeValidator.NoTitle, KnowledgeValidator.UnknownOption, KnowledgeValidator.ViewNamesMissingProperty }, codes);
    }

    [Fact]
    public async Task WhatIsReported_IsStillInTheFile_AfterAnEditOfSomethingElse()
    {
        // Arrange: a word where a number belongs, an option that is not there, and a cell of a property that is not there.
        var path = Write(Sound
            .Replace("        number: 931298\n", "        number: a great many\n", StringComparison.Ordinal)
            .Replace("        option: o1\n", "        option: o9\n", StringComparison.Ordinal)
            .Replace("        text: Antwerp\n", "        text: Antwerp\n      - property: p9\n        text: stray\n", StringComparison.Ordinal));
        var before = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);
        await using var table = new EditingTable(path);

        // Act: an edit of another cell altogether.
        await table.Edit(new TableGesture("setCell", RowId: "r2", ColumnId: "p1", Values: ["Antwerpen"]));

        // Assert: one line changed, and every line that was reported is the line it was.
        var after = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);
        Assert.Equal(before.Replace("text: Antwerp\r\n", "text: Antwerpen\r\n", StringComparison.Ordinal), after);
        Assert.Contains("number: a great many", after, StringComparison.Ordinal);
        Assert.Contains("option: o9", after, StringComparison.Ordinal);
        Assert.Contains("- property: p9", after, StringComparison.Ordinal);
    }
}
