using EtAlii.Adp.Designer.TableModel;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Designer.Knowledge.Tests;

/// <summary>
/// Changing a property's type (knowledge-designer Requirements 3.3 and 3.4): every value that has
/// a meaning in the new type is converted in the same step, and every other value stays in the
/// file under the key it had. No value is discarded.
/// </summary>
/// <remarks>
/// <b>The cases are the conversion table of <c>definition/knowledge.md</c>, read from it.</b> Each
/// of its cells says <i>kept</i> or says what the value becomes, and each is a case here: a table
/// that gained a conversion the module does not make, or the module making one the table does not
/// have, fails under the name of that cell.
/// </remarks>
public sealed class KnowledgeTypeChangeTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("adp-knowledge-types-").FullName;

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

    /// <summary>A cell as text: the key it is held under and what it holds. A record holding a list is equal only to itself.</summary>
    private static string Show(KnowledgeCell cell) => $"{cell.Key}: {string.Join(" | ", cell.Values)}";

    /// <summary>The table's headings, and the value types each stands for.</summary>
    private static readonly Dictionary<string, string[]> Headings = new(StringComparer.Ordinal)
    {
        ["Text"] = ["text"],
        ["Number"] = ["number"],
        ["Checkbox"] = ["checkbox"],
        ["Date, date and time, time"] = ["date", "dateTime", "time"],
        ["Selection"] = ["selection"],
        ["Multiple selection"] = ["multipleSelection"],
        ["Relation"] = ["relation"],
    };

    /// <summary>The conversion table: for each type and each other type, whether the value is kept.</summary>
    private static List<(string From, string To, bool Kept)> ConversionTable()
    {
        var lines = File.ReadAllLines(IoPath.Combine(KnowledgeFiles.DefinitionFolder, "knowledge.md"));
        var start = Array.FindIndex(lines, line => line.StartsWith("| From", StringComparison.Ordinal));
        Assert.True(start >= 0, "knowledge.md has no conversion table.");

        var columns = Cells(lines[start])[1..];
        List<(string, string, bool)> table = [];
        for (var index = start + 2; index < lines.Length && lines[index].StartsWith('|'); index++)
        {
            var cells = Cells(lines[index]);
            for (var column = 0; column < columns.Length; column++)
            {
                if (cells[column + 1].Length == 0)
                {
                    continue;
                }

                foreach (var from in Headings[cells[0]])
                {
                    foreach (var to in Headings[columns[column]].Where(to => to != from))
                    {
                        table.Add((from, to, cells[column + 1].StartsWith("kept", StringComparison.Ordinal)));
                    }
                }
            }
        }

        return table;

        static string[] Cells(string line) => [.. line.Trim().Trim('|').Split('|').Select(cell => cell.Trim())];
    }

    /// <summary>
    /// Every pair of the table but a relation's: a relation is made by choosing its target and
    /// its values are another file's rows, which the relations' own tests cover.
    /// </summary>
    public static TheoryData<string, string, bool> Conversions()
    {
        var data = new TheoryData<string, string, bool>();
        foreach ((string from, string to, bool kept) in ConversionTable().Where(pair => pair.From != "relation" && pair.To != "relation"))
        {
            data.Add(from, to, kept);
        }

        return data;
    }

    [Fact]
    public void TheConversionTable_IsRead_AndCoversEveryPairOfTypes()
    {
        // Act.
        var table = ConversionTable();

        // Assert: nine types, each to each of the eight others, each said once.
        Assert.Equal(72, table.Count);
        Assert.Equal(72, table.Select(pair => (pair.From, pair.To)).Distinct().Count());
        Assert.Equal(KnowledgeVocabulary.ValueTypes.Order(StringComparer.Ordinal), table.Select(pair => pair.From).Distinct().Order(StringComparer.Ordinal));

        // And it says both things: a table read as all kept, or as all converted, would pass every case below for nothing.
        Assert.Contains(table, pair => pair.Kept);
        Assert.Contains(table, pair => !pair.Kept);
    }

    /// <summary>The shipped example's property of each type, the row to look at, and what that row holds there.</summary>
    private static (string PropertyId, string RowId) Sample(string from, string to) => from switch
    {
        "number" => ("p3", "r1"),
        "checkbox" => ("p6", "r1"),
        "date" => ("p7", "r1"),
        "dateTime" => ("p8", "r1"),
        "time" => ("p9", "r1"),
        "selection" => ("p2", "r1"),
        // One tag in the second row, two in the first: one is what converts to a selection.
        "multipleSelection" => ("p4", to == "selection" ? "r2" : "r1"),
        _ => ("", "r1"),
    };

    /// <summary>A text that has a meaning in the type it is to become.</summary>
    private static string TextFor(string to) => to switch
    {
        "number" => "42",
        "checkbox" => "true",
        "date" => "2026-01-02",
        "dateTime" => "2026-01-02T03:04:05+00:00",
        "time" => "03:04",
        _ => "Port",
    };

    [Theory]
    [MemberData(nameof(Conversions))]
    public async Task AValue_IsConvertedOrKept_AsTheTableSays(string from, string to, bool kept)
    {
        // Arrange: a property of the type with a value, the example's own or a text made for the case.
        await using var table = new EditingTable(KnowledgeFiles.CopyExample("cities.yaml", _root));
        (string propertyId, string rowId) = Sample(from, to);
        if (from == "text")
        {
            await table.Edit(new TableGesture("addColumn", Settings: new Dictionary<string, string> { ["type"] = "text" }));
            propertyId = table.OnDisk().Properties[^1].Id;
            await table.Edit(new TableGesture("setCell", RowId: rowId, ColumnId: propertyId, Values: [TextFor(to)]));
        }

        var before = table.OnDisk().Rows.Single(row => row.Id == rowId).Cells.Single(cell => cell.PropertyId == propertyId);
        Assert.Equal(KnowledgeEdits.KeyOf(from), before.Key);

        // Act.
        await table.Edit(new TableGesture("setColumnType", ColumnId: propertyId, Settings: new Dictionary<string, string> { ["type"] = to }));

        // Assert: the property has the type, and the value is under the new type's key or still under its own.
        var disk = table.OnDisk();
        Assert.Equal(to, disk.Properties.Single(property => property.Id == propertyId).ValueType);
        var after = disk.Rows.Single(row => row.Id == rowId).Cells.Single(cell => cell.PropertyId == propertyId);
        Assert.NotEmpty(after.Values);
        if (kept)
        {
            Assert.Equal(before.Key, after.Key);
            Assert.Equal(before.Values, after.Values);
        }
        else
        {
            Assert.Equal(KnowledgeEdits.KeyOf(to), after.Key);
        }

        // One step, whatever it converted.
        await table.History.UndoAsync(TestContext.Current.CancellationToken);
        Assert.Equal(from, table.OnDisk().Properties.Single(property => property.Id == propertyId).ValueType);
        Assert.Equal(Show(before), Show(table.OnDisk().Rows.Single(row => row.Id == rowId).Cells.Single(cell => cell.PropertyId == propertyId)));
    }

    [Theory]
    [MemberData(nameof(KnowledgeFiles.Extensions), MemberType = typeof(KnowledgeFiles))]
    public async Task AValueWithoutAMeaningInTheNewType_IsStillInTheFile_AndThereAgainWhenTheTypeChangesBack(string extension)
    {
        // Arrange: a text property with a number in one row and a word in the other.
        await using var table = new EditingTable(KnowledgeFiles.CopyExample("cities" + extension, _root));
        await table.Edit(new TableGesture("addColumn", Settings: new Dictionary<string, string> { ["type"] = "text" }));
        var id = table.OnDisk().Properties[^1].Id;
        await table.Edit(new TableGesture("setCell", RowId: "r1", ColumnId: id, Values: ["42"]));
        await table.Edit(new TableGesture("setCell", RowId: "r2", ColumnId: id, Values: ["a great many"]));

        // Act.
        await table.Edit(new TableGesture("setColumnType", ColumnId: id, Settings: new Dictionary<string, string> { ["type"] = "number" }));

        // Assert: the number is a number, and the word is the word it was, as text.
        Assert.Equal("number: 42", Show(Cell("r1")));
        Assert.Equal("text: a great many", Show(Cell("r2")));
        Assert.Contains("a great many", await File.ReadAllTextAsync(table.Path, TestContext.Current.CancellationToken), StringComparison.Ordinal);

        // Act: back to text.
        await table.Edit(new TableGesture("setColumnType", ColumnId: id, Settings: new Dictionary<string, string> { ["type"] = "text" }));

        // Assert: both are text again, and neither was lost on the way.
        Assert.Equal("text: 42", Show(Cell("r1")));
        Assert.Equal("text: a great many", Show(Cell("r2")));
        return;

        KnowledgeCell Cell(string rowId) => table.OnDisk().Rows.Single(row => row.Id == rowId).Cells.Single(cell => cell.PropertyId == id);
    }

    [Theory]
    [MemberData(nameof(KnowledgeFiles.Extensions), MemberType = typeof(KnowledgeFiles))]
    public async Task TextBecomingASelection_GetsTheOptionOfThatName_MadeOnceWhenThereIsNone(string extension)
    {
        // Arrange: the same word in both rows.
        await using var table = new EditingTable(KnowledgeFiles.CopyExample("cities" + extension, _root));
        await table.Edit(new TableGesture("addColumn", Settings: new Dictionary<string, string> { ["type"] = "text" }));
        var id = table.OnDisk().Properties[^1].Id;
        await table.Edit(new TableGesture("setCell", RowId: "r1", ColumnId: id, Values: ["Large"]));
        await table.Edit(new TableGesture("setCell", RowId: "r2", ColumnId: id, Values: ["large"]));

        // Act.
        await table.Edit(new TableGesture("setColumnType", ColumnId: id, Settings: new Dictionary<string, string> { ["type"] = "selection" }));

        // Assert: one option, named as it was first written, and both rows have it.
        var disk = table.OnDisk();
        var option = Assert.Single(disk.Properties.Single(property => property.Id == id).Options);
        Assert.Equal("Large", option.Name);
        Assert.All(disk.Rows, row => Assert.Equal([option.Id], row.Cells.Single(cell => cell.PropertyId == id).Values));
    }

    [Theory]
    [MemberData(nameof(KnowledgeFiles.Extensions), MemberType = typeof(KnowledgeFiles))]
    public async Task SeveralOptionsBecomingText_AreTheirNames_AndACellSetAfterwardsHoldsOneValue(string extension)
    {
        // Arrange: r1 has the tags Port and Capital.
        await using var table = new EditingTable(KnowledgeFiles.CopyExample("cities" + extension, _root));

        // Act.
        await table.Edit(new TableGesture("setColumnType", ColumnId: "p4", Settings: new Dictionary<string, string> { ["type"] = "text" }));

        // Assert.
        Assert.Equal("text: Port, Capital", Show(Cell("r1", "p4")));

        // Act: a number becomes a checkbox, which keeps it; ticking the box then replaces it.
        await table.Edit(new TableGesture("setColumnType", ColumnId: "p3", Settings: new Dictionary<string, string> { ["type"] = "checkbox" }));
        Assert.Equal("number: 931298", Show(Cell("r1", "p3")));
        await table.Edit(new TableGesture("setCell", RowId: "r1", ColumnId: "p3", Values: ["true"]));

        // Assert: one value, the new one.
        Assert.Equal("checked: true", Show(Cell("r1", "p3")));
        Assert.DoesNotContain("931298", await File.ReadAllTextAsync(table.Path, TestContext.Current.CancellationToken), StringComparison.Ordinal);
        return;

        KnowledgeCell Cell(string rowId, string propertyId) => table.OnDisk().Rows.Single(row => row.Id == rowId).Cells.Single(cell => cell.PropertyId == propertyId);
    }

    [Fact]
    public async Task TheTitlePropertysType_DoesNotChange_AndNothingBecomesARelationFromTheMenu()
    {
        // Arrange.
        await using var table = new EditingTable(KnowledgeFiles.CopyExample("cities.yaml", _root));
        var before = table.Bytes();

        // Act.
        (_, string title) = table.Begin(new TableGesture("setColumnType", ColumnId: "p1", Settings: new Dictionary<string, string> { ["type"] = "number" }));
        (_, string relation) = table.Begin(new TableGesture("setColumnType", ColumnId: "p3", Settings: new Dictionary<string, string> { ["type"] = "relation" }));

        // Assert.
        Assert.Equal(KnowledgeEdits.TitleIsText, title);
        Assert.Equal(KnowledgeEdits.RelationNeedsTarget, relation);
        Assert.Equal(before, table.Bytes());
    }
}
