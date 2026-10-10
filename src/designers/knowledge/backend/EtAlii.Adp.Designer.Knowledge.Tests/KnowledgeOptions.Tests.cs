using System.Text;
using EtAlii.Adp.Designer.TableModel;
using Xunit;

namespace EtAlii.Adp.Designer.Knowledge.Tests;

/// <summary>
/// A selection's options (knowledge-designer Requirements 3.5 and 3.6): added, renamed, recoloured
/// and reordered without touching any row - rows hold an option's id, never its name - and deleted
/// together with every value that names it, in one step.
/// </summary>
public sealed class KnowledgeOptionsTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("adp-knowledge-options-").FullName;

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

    private EditingTable Open(string extension) => new(KnowledgeFiles.CopyPlain("cities" + extension, _root));

    private static TableGesture Option(string kind, string propertyId, string optionId = "", string? name = null, string? colour = null, int index = 0) => new(
        kind,
        ColumnId: propertyId,
        TargetId: optionId,
        Values: name is null ? [] : [name],
        Index: index,
        Settings: colour is null ? null : new Dictionary<string, string> { ["colour"] = colour });

    private static IReadOnlyList<KnowledgeOption> Options(EditingTable table, string propertyId) => table.OnDisk().Properties.Single(property => property.Id == propertyId).Options;

    [Theory]
    [MemberData(nameof(KnowledgeFiles.Extensions), MemberType = typeof(KnowledgeFiles))]
    public async Task ARenamedOption_ChangesOnePlaceInTheFile_AndNoRow(string extension)
    {
        // Arrange: both rows name an option of Country by its id.
        await using var table = Open(extension);
        var before = Encoding.UTF8.GetString(table.Bytes());
        var rows = table.OnDisk().Rows;

        // Act.
        await table.Edit(Option("renameOption", "p2", "o1", "The Netherlands"));

        // Assert: the name where it was, and nothing else.
        Assert.Equal(before.Replace("Netherlands", "The Netherlands", StringComparison.Ordinal), Encoding.UTF8.GetString(table.Bytes()));
        Assert.Equal(rows.Select(row => string.Join("|", row.Cells.Select(cell => $"{cell.PropertyId}={string.Join(",", cell.Values)}"))), table.OnDisk().Rows.Select(row => string.Join("|", row.Cells.Select(cell => $"{cell.PropertyId}={string.Join(",", cell.Values)}"))));
    }

    [Theory]
    [MemberData(nameof(KnowledgeFiles.Extensions), MemberType = typeof(KnowledgeFiles))]
    public async Task Options_AreAdded_Recoloured_AndReordered(string extension)
    {
        // Arrange.
        await using var table = Open(extension);

        // Act.
        await table.Edit(Option("addOption", "p2", name: "Luxembourg"));
        var added = Options(table, "p2")[2];
        await table.Edit(Option("recolourOption", "p2", added.Id, colour: "green"));
        await table.Edit(Option("recolourOption", "p2", "o1", colour: "default"));
        await table.Edit(Option("moveOption", "p2", added.Id, index: 0));

        // Assert: an id of its own, first of the three, green; and blue given up for the default.
        Assert.DoesNotContain(added.Id, new[] { "o1", "o2", "o3", "o4" });
        Assert.Equal(["Luxembourg:green", "Netherlands:default", "Belgium:yellow"], Options(table, "p2").Select(option => $"{option.Name}:{option.Colour}"));
        Assert.Empty(KnowledgeDocumentStore.Read(table.Path).Body!.Model.Findings);
    }

    [Theory]
    [MemberData(nameof(KnowledgeFiles.Extensions), MemberType = typeof(KnowledgeFiles))]
    public async Task ADeletedOption_TakesEveryValueThatNamesItWithIt_InOneStep(string extension)
    {
        // Arrange: Port is a tag of both rows, and v1 filters on Netherlands and folds its group.
        await using var table = Open(extension);
        await table.Edit(new TableGesture("toggleGroup", ViewId: "v1", TargetId: "o1", Settings: new Dictionary<string, string> { ["collapsed"] = "true" }));
        var before = table.Bytes();

        // Act.
        await table.Edit(Option("deleteOption", "p4", "o3"));
        await table.Edit(Option("deleteOption", "p2", "o1"));

        // Assert: no option, no value, no condition and no group setting names either any more, and nothing is reported.
        var body = KnowledgeDocumentStore.Read(table.Path).Body!;
        Assert.Equal(["Capital"], body.Table.Properties.Single(property => property.Id == "p4").Options.Select(option => option.Name));
        Assert.Equal(["Belgium"], body.Table.Properties.Single(property => property.Id == "p2").Options.Select(option => option.Name));
        Assert.DoesNotContain(body.Model.Elements, element => element.Attributes.Values.Any(value => KnowledgeValues.Text(value) is "o1" or "o3"));
        var view = body.Table.Views.Single(candidate => candidate.Id == "v1");
        Assert.Empty(view.Filter.Items);
        Assert.Empty(view.Collapsed);
        Assert.Empty(body.Model.Findings);
        Assert.Empty(KnowledgeValidator.Validate(body.Table, body.Model));

        // The other tag of the first row is still its tag.
        Assert.Equal(["o4"], body.Table.Rows.Single(row => row.Id == "r1").Cells.Single(cell => cell.PropertyId == "p4").Values);

        // Each delete is one step.
        await table.History.UndoAsync(TestContext.Current.CancellationToken);
        await table.History.UndoAsync(TestContext.Current.CancellationToken);
        Assert.Equal(before, table.Bytes());
    }

    [Theory]
    [MemberData(nameof(KnowledgeFiles.Extensions), MemberType = typeof(KnowledgeFiles))]
    public async Task AnOptionSomeRowsHave_SaysHowMany_SoItsAuthorIsToldBeforeDeletingIt(string extension)
    {
        // Arrange: Port is a tag of both rows, Capital of one; Netherlands and Belgium are each one row's country.
        await using var table = Open(extension);
        IReadOnlyDictionary<string, string> Settings(string id) => table.Session.Baseline().Columns.Single(column => column.Id == id).Settings;

        // Assert.
        Assert.Equal(("2", "1"), (Settings("p4")["uses:o3"], Settings("p4")["uses:o4"]));
        Assert.Equal(("1", "1"), (Settings("p2")["uses:o1"], Settings("p2")["uses:o2"]));

        // Act: an option nobody has says nothing, and the count follows the rows.
        await table.Edit(Option("addOption", "p2", name: "Luxembourg"));
        await table.Edit(new TableGesture("setCell", RowId: "r1", ColumnId: "p4", Values: ["o4"]));

        // Assert.
        Assert.Equal(2, Settings("p2").Keys.Count(key => key.StartsWith("uses:", StringComparison.Ordinal)));
        Assert.Equal("1", Settings("p4")["uses:o3"]);
    }

    [Fact]
    public async Task AnOption_NeedsAName_ThatNoOtherOptionOfItsPropertyHas_AndAColourThereIs()
    {
        // Arrange.
        await using var table = Open(".yaml");
        var before = table.Bytes();

        // Act.

        // Assert.
        Assert.Equal(KnowledgeEdits.OptionNameNeeded, Answer(Option("addOption", "p2", name: "  ")));
        Assert.Equal("'Country' already has an option called 'belgium'.", Answer(Option("addOption", "p2", name: "belgium")));
        Assert.Equal("'Country' already has an option called 'Belgium'.", Answer(Option("renameOption", "p2", "o1", "Belgium")));
        Assert.Equal("'mauve' is not a colour an option can have.", Answer(Option("recolourOption", "p2", "o1", colour: "mauve")));
        Assert.Equal("Only a selection has options.", Answer(Option("addOption", "p3", name: "Many")));
        Assert.Equal("That option is no longer one of this property's.", Answer(Option("renameOption", "p2", "o3", "Harbour")));
        Assert.Equal(before, table.Bytes());
        Assert.False(table.History.CanUndo);
        return;

        string Answer(TableGesture gesture) => table.Begin(gesture).Answer;
    }
}
