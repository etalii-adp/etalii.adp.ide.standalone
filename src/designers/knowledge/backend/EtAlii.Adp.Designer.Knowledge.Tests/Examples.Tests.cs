using EtAlii.Adp.Designer.TableModel;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Designer.Knowledge.Tests;

/// <summary>
/// The shipped examples (knowledge-designer Requirements 9.1 to 9.4): every one reads with
/// nothing to report and, written unchanged, keeps its bytes; the same table read from each of
/// the three formats is the same table, and is again after each kind of edit; and every example
/// in the showcase is registered, so it opens from the explorer.
/// </summary>
public sealed class ExamplesTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("adp-knowledge-examples-").FullName;

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

    /// <summary>Every data file of the module's examples and of the showcase's, by its path from <c>src</c>.</summary>
    public static TheoryData<string> DataFiles()
    {
        var data = new TheoryData<string>();
        foreach (var folder in new[] { KnowledgeFiles.ExamplesFolder, KnowledgeFiles.ShowcaseFolder })
        {
            foreach (var file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Where(file => IoPath.GetExtension(file) is ".yaml" or ".json" or ".xml").Order(StringComparer.Ordinal))
            {
                data.Add(IoPath.GetRelativePath(KnowledgeFiles.SourceFolder, file).Replace('\\', '/'));
            }
        }

        return data;
    }

    [Fact]
    public void TheExamples_AreFound_InTheModuleAndInTheShowcase()
    {
        // Assert: the canary. Three formats in each place; a walk that found fewer has stopped looking.
        Assert.Equal(6, DataFiles().Count);
    }

    [Theory]
    [MemberData(nameof(DataFiles))]
    public void AnExample_ReadsWithNothingToReport_AndWrittenUnchangedKeepsItsBytes(string file)
    {
        // Arrange.
        var path = IoPath.Combine(KnowledgeFiles.SourceFolder, file);
        var bytes = File.ReadAllBytes(path);

        // Act.
        var body = KnowledgeBody.Read(bytes, path);

        // Assert: readable, editable, and neither the reading nor the designer's rules have anything to say.
        Assert.Equal("", body.ReadOnlyReason);
        Assert.Empty(body.Model.Findings);
        Assert.Empty(KnowledgeValidator.Validate(body.Table, body.Model));
        Assert.NotEmpty(body.Table.Rows);

        // Written with nothing changed, it is the bytes it was.
        (byte[]? written, string refusal) = body.Change([]);
        Assert.Equal("", refusal);
        Assert.Equal(bytes, written);
    }

    [Fact]
    public void TheShowcasesExamples_AreTheModulesExamples()
    {
        // Assert: the same table, whatever a checkout did to the line endings of the showcase's copy.
        foreach (var extension in new[] { ".yaml", ".json", ".xml" })
        {
            var module = KnowledgeBody.Read(KnowledgeFiles.ExampleBytes("cities" + extension), "cities" + extension).Table;
            var showcase = IoPath.Combine(KnowledgeFiles.ShowcaseFolder, extension.TrimStart('.'), "cities" + extension);
            Assert.Equal(KnowledgeFiles.Describe(module), KnowledgeFiles.Describe(KnowledgeBody.Read(File.ReadAllBytes(showcase), showcase).Table));
        }
    }

    [Fact]
    public void EveryExampleInTheShowcase_IsRegistered_ByAFileThatNamesItAndTheDesigner()
    {
        // Arrange.
        var bodies = Directory.EnumerateFiles(KnowledgeFiles.ShowcaseFolder, "cities.*", SearchOption.AllDirectories).Where(file => IoPath.GetExtension(file) != ".adp").ToList();

        // Assert: beside each data file, a registration of two lines - the designer's origin, and the file it is the registration of.
        Assert.Equal(3, bodies.Count);
        foreach (var body in bodies)
        {
            var registration = IoPath.ChangeExtension(body, ".adp");
            Assert.True(File.Exists(registration), $"{body} has no registration beside it.");
            Assert.Equal([Designer.Origin, $"body: {IoPath.GetFileName(body)}"], File.ReadAllLines(registration));
        }
    }

    [Fact]
    public void TheSameTable_ReadFromEachFormat_IsTheSameTable()
    {
        // Act.
        var tables = new[] { ".yaml", ".json", ".xml" }.Select(extension => KnowledgeFiles.Describe(KnowledgeBody.Read(KnowledgeFiles.ExampleBytes("cities" + extension), "cities" + extension).Table)).ToList();

        // Assert.
        Assert.Equal(tables[0], tables[1]);
        Assert.Equal(tables[0], tables[2]);
        Assert.Contains("row r1", tables[0], StringComparison.Ordinal);
    }

    /// <summary>
    /// One edit of each kind that makes nothing new - what is made new is given an id that was never
    /// used, which differs from file to file by design.
    /// </summary>
    public static TheoryData<string> Edits() =>
    [
        "setCell", "clearCell", "setSeveral", "renameColumn", "setColumnType", "deleteColumn", "deleteRows",
        "renameOption", "recolourOption", "moveOption", "deleteOption",
        "renameView", "moveView", "deleteView", "hideColumn", "resizeColumn", "moveColumn",
        "addSort", "removeSort", "addFilter", "setFilter", "removeFilter", "groupBy", "toggleGroup",
    ];

    private static TableGesture Gesture(string edit)
    {
        static Dictionary<string, string> Settings(string name, string value) => new() { [name] = value };
        return edit switch
        {
            "setCell" => new("setCell", RowId: "r2", ColumnId: "p3", Values: ["529247"]),
            "clearCell" => new("setCell", RowId: "r1", ColumnId: "p7"),
            "setSeveral" => new("setCell", RowId: "r2", ColumnId: "p4", Values: ["o4"]),
            "renameColumn" => new("renameColumn", ColumnId: "p3", Values: ["Inhabitants"]),
            "setColumnType" => new("setColumnType", ColumnId: "p3", Settings: Settings("type", "text")),
            "deleteColumn" => new("deleteColumn", ColumnId: "p2"),
            "deleteRows" => new("deleteRows", Values: ["r1"]),
            "renameOption" => new("renameOption", ColumnId: "p2", TargetId: "o2", Values: ["Flanders"]),
            "recolourOption" => new("recolourOption", ColumnId: "p4", TargetId: "o3", Settings: Settings("colour", "green")),
            "moveOption" => new("moveOption", ColumnId: "p2", TargetId: "o2", Index: 0),
            "deleteOption" => new("deleteOption", ColumnId: "p4", TargetId: "o3"),
            "renameView" => new("renameView", ViewId: "v2", Values: ["Everything"]),
            "moveView" => new("moveView", ViewId: "v2", Index: 0),
            "deleteView" => new("deleteView", ViewId: "v1"),
            "hideColumn" => new("hideColumn", ColumnId: "p5", ViewId: "v1"),
            "resizeColumn" => new("resizeColumn", ColumnId: "p1", ViewId: "v2", Settings: Settings("width", "260")),
            "moveColumn" => new("moveColumn", ColumnId: "p6", ViewId: "v1", Index: 0),
            "addSort" => new("addSort", ColumnId: "p1", ViewId: "v1", Settings: Settings("direction", "descending")),
            "removeSort" => new("removeSort", ColumnId: "p3", ViewId: "v1"),
            "addFilter" => new("addFilter", ColumnId: "p6", ViewId: "v1"),
            "setFilter" => new("setFilter", ViewId: "v1", TargetId: "0", Values: ["o2"], Settings: Settings("comparison", "is-not")),
            "removeFilter" => new("removeFilter", ViewId: "v1", TargetId: "0"),
            "groupBy" => new("groupBy", ColumnId: "p6", ViewId: "v1"),
            "toggleGroup" => new("toggleGroup", ViewId: "v1", TargetId: "o2", Settings: Settings("collapsed", "true")),
            _ => throw new InvalidOperationException($"No gesture is written for '{edit}'."),
        };
    }

    [Theory]
    [MemberData(nameof(Edits))]
    public async Task TheSameEdit_MadeInEachFormat_LeavesTheSameTable(string edit)
    {
        // Act: the edit, in a copy of the example in each format.
        List<string> tables = [];
        foreach (var extension in new[] { ".yaml", ".json", ".xml" })
        {
            await using var table = new EditingTable(KnowledgeFiles.CopyExample("cities" + extension, Directory.CreateDirectory(IoPath.Combine(_root, edit + extension)).FullName));
            await table.Edit(Gesture(edit));
            tables.Add(KnowledgeFiles.Describe(table.OnDisk()));
        }

        // Assert: one table, three times - and not the table it was, or the edit proved nothing.
        Assert.Equal(tables[0], tables[1]);
        Assert.Equal(tables[0], tables[2]);
        Assert.NotEqual(KnowledgeFiles.Describe(KnowledgeBody.Read(KnowledgeFiles.ExampleBytes("cities.yaml"), "cities.yaml").Table), tables[0]);
    }
}
