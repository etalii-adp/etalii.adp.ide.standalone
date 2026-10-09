using System.Text;
using EtAlii.Adp.Designer.TableModel;
using EtAlii.Adp.History;
using Xunit;

namespace EtAlii.Adp.Designer.Knowledge.Tests;

/// <summary>
/// Editing a knowledge file (knowledge-designer Requirements 3, 4 and 6): every gesture of the
/// table, made through a session as the host makes it, over each of the three formats - what the
/// file holds afterwards, that an undo gives its bytes back exactly, and what is refused.
/// </summary>
/// <remarks>
/// A test reads the file back with the module's own reading rather than comparing text, so one
/// assertion holds for YAML, JSON and XML alike; the bytes are compared where bytes are the claim -
/// an undo, a refusal, and a file that was not to be touched.
/// </remarks>
public sealed class KnowledgeEditingTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("adp-knowledge-editing-").FullName;

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

    /// <summary>The example, left in its plain view: every row, in the file's order. A test that is about a view names the view.</summary>
    private EditingTable Open(string extension) => new(KnowledgeFiles.CopyPlain("cities" + extension, _root));

    private static IReadOnlyList<string> Cell(KnowledgeTable table, string rowId, string propertyId) =>
        table.Rows.Single(row => row.Id == rowId).Cells.FirstOrDefault(cell => cell.PropertyId == propertyId)?.Values ?? [];

    private static TableGesture SetCell(string rowId, string columnId, params string[] values) => new("setCell", RowId: rowId, ColumnId: columnId, Values: values);

    private static Dictionary<string, string> Settings(params (string Name, string Value)[] settings) => settings.ToDictionary(setting => setting.Name, setting => setting.Value);

    // ---- cells ----------------------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(KnowledgeFiles.Extensions), MemberType = typeof(KnowledgeFiles))]
    public async Task ACell_IsSet_Written_AndUndoneToTheBytesItHad(string extension)
    {
        // Arrange.
        await using var table = Open(extension);
        var before = table.Bytes();

        // Act.
        var settled = await table.Edit(SetCell("r2", "p1", "Antwerpen"));

        // Assert: written, and nothing else in the file moved.
        Assert.True(settled.Written);
        Assert.Equal(["Antwerpen"], Cell(table.OnDisk(), "r2", "p1"));
        Assert.Equal(Encoding.UTF8.GetString(before).Replace("Antwerp", "Antwerpen", StringComparison.Ordinal), Encoding.UTF8.GetString(table.Bytes()));

        // Act: the application's undo, and its redo.
        var undone = await table.History.UndoAsync(TestContext.Current.CancellationToken);

        // Assert.
        Assert.True(undone.IsSuccess);
        Assert.Equal(before, table.Bytes());

        var redone = await table.History.RedoAsync(TestContext.Current.CancellationToken);
        Assert.True(redone.IsSuccess);
        Assert.Equal(["Antwerpen"], Cell(table.OnDisk(), "r2", "p1"));
    }

    [Theory]
    [MemberData(nameof(KnowledgeFiles.Extensions), MemberType = typeof(KnowledgeFiles))]
    public async Task ACellOfEachType_IsWrittenInItsOwnForm(string extension)
    {
        // Arrange: r2 has a name, a country and one tag, and nothing else.
        await using var table = Open(extension);

        // Act.
        await table.Edit(SetCell("r2", "p3", "529247"));
        await table.Edit(SetCell("r2", "p6", "true"));
        await table.Edit(SetCell("r2", "p7", "1291-02-21"));
        await table.Edit(SetCell("r2", "p8", "2026-10-09T08:15:00+02:00"));
        await table.Edit(SetCell("r2", "p9", "07:30"));
        await table.Edit(SetCell("r2", "p2", "o1"));
        await table.Edit(SetCell("r2", "p4", "o3", "o4"));

        // Assert.
        var disk = table.OnDisk();
        Assert.Equal(["529247"], Cell(disk, "r2", "p3"));
        Assert.Equal(["true"], Cell(disk, "r2", "p6"));
        Assert.Equal(["1291-02-21"], Cell(disk, "r2", "p7"));
        Assert.Equal(["2026-10-09T08:15:00+02:00"], Cell(disk, "r2", "p8"));
        Assert.Equal(["07:30"], Cell(disk, "r2", "p9"));
        Assert.Equal(["o1"], Cell(disk, "r2", "p2"));
        Assert.Equal(["o3", "o4"], Cell(disk, "r2", "p4"));
        Assert.Empty(KnowledgeDocumentStore.Read(table.Path).Body!.Model.Findings);
    }

    [Theory]
    [MemberData(nameof(KnowledgeFiles.Extensions), MemberType = typeof(KnowledgeFiles))]
    public async Task ACell_IsCleared_AndOneOfSeveralValuesIsTakenOut(string extension)
    {
        // Arrange.
        await using var table = Open(extension);

        // Act: a number cleared, a tick taken away, and one of two tags removed.
        await table.Edit(SetCell("r1", "p3"));
        await table.Edit(SetCell("r1", "p6", "false"));
        await table.Edit(SetCell("r1", "p4", "o4"));

        // Assert.
        var disk = table.OnDisk();
        Assert.Empty(Cell(disk, "r1", "p3"));
        Assert.Empty(Cell(disk, "r1", "p6"));
        Assert.Equal(["o4"], Cell(disk, "r1", "p4"));
        Assert.Equal(["Amsterdam"], Cell(disk, "r1", "p1"));
        Assert.Empty(KnowledgeDocumentStore.Read(table.Path).Body!.Model.Findings);
    }

    [Fact]
    public async Task ADateAndTimeGivenWithoutAnOffset_IsWrittenWithThisMachines()
    {
        // Arrange.
        await using var table = Open(".yaml");

        // Act: what the browser's own date and time input gives.
        await table.Edit(SetCell("r2", "p8", "2026-03-01T12:00"));

        // Assert: ISO 8601 with its offset.
        var written = Assert.Single(Cell(table.OnDisk(), "r2", "p8"));
        Assert.Matches(@"^2026-03-01T12:00:00[+-]\d\d:\d\d$", written);
        Assert.Equal(new DateTime(2026, 3, 1, 12, 0, 0), DateTimeOffset.Parse(written, System.Globalization.CultureInfo.InvariantCulture).DateTime);
    }

    [Theory]
    [InlineData("p3", "many", KnowledgeEdits.NumberExpected)]
    [InlineData("p7", "27 October", KnowledgeEdits.DateExpected)]
    [InlineData("p8", "yesterday", KnowledgeEdits.DateTimeExpected)]
    [InlineData("p9", "noon", KnowledgeEdits.TimeExpected)]
    [InlineData("p2", "nothing-like-an-option", "That option is no longer one of this property's.")]
    public async Task AValueThatIsNotOfItsType_IsRefused_AndNothingIsWritten(string propertyId, string value, string refusal)
    {
        // Arrange.
        await using var table = Open(".yaml");
        var before = table.Bytes();

        // Act.
        (_, string answer) = table.Begin(SetCell("r1", propertyId, value));

        // Assert.
        Assert.Equal(refusal, answer);
        Assert.Equal(before, table.Bytes());
        Assert.False(table.History.CanUndo);
    }

    [Theory]
    [MemberData(nameof(KnowledgeFiles.Extensions), MemberType = typeof(KnowledgeFiles))]
    public async Task AnOptionMadeWhileFillingInACell_IsMadeAndChosenInOneStep(string extension)
    {
        // Arrange.
        await using var table = Open(extension);
        var before = table.Bytes();

        // Act.
        await table.Edit(new TableGesture("setCell", RowId: "r2", ColumnId: "p2", Settings: Settings(("newOption", "Luxembourg"))));

        // Assert: the option exists once, with an id of its own, and the cell names it.
        var disk = table.OnDisk();
        var option = Assert.Single(disk.Properties.Single(property => property.Id == "p2").Options, candidate => candidate.Name == "Luxembourg");
        Assert.DoesNotContain(option.Id, new[] { "o1", "o2", "o3", "o4" });
        Assert.Equal([option.Id], Cell(disk, "r2", "p2"));

        // One step: one undo takes back both.
        await table.History.UndoAsync(TestContext.Current.CancellationToken);
        Assert.Equal(before, table.Bytes());
    }

    // ---- rows -----------------------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(KnowledgeFiles.Extensions), MemberType = typeof(KnowledgeFiles))]
    public async Task ARow_IsAddedAtTheEnd_AfterAnother_AndInAGroupWithItsValue(string extension)
    {
        // Arrange.
        await using var table = Open(extension);

        // Act.
        await table.Edit(new TableGesture("addRow"));
        await table.Edit(new TableGesture("addRow", TargetId: "r1"));
        await table.Edit(new TableGesture("addRow", ViewId: "v1", Settings: Settings(("group", "o2"))));

        // Assert: three new rows, each with an id never used; one after r1, the others at the end.
        var rows = table.OnDisk().Rows;
        Assert.Equal(5, rows.Count);
        Assert.Equal("r1", rows[0].Id);
        Assert.Equal("r2", rows[2].Id);
        Assert.Equal(5, rows.Select(row => row.Id).Distinct().Count());

        // The row added in the group of Belgium is a row of Belgium.
        Assert.Equal(["o2"], Cell(table.OnDisk(), rows[4].Id, "p2"));
        Assert.Empty(rows[1].Cells);
        Assert.Empty(rows[3].Cells);
    }

    [Theory]
    [MemberData(nameof(KnowledgeFiles.Extensions), MemberType = typeof(KnowledgeFiles))]
    public async Task Rows_AreDeletedInOneStep(string extension)
    {
        // Arrange.
        await using var table = Open(extension);
        var before = table.Bytes();

        // Act.
        await table.Edit(new TableGesture("deleteRows", Values: ["r1", "r2"]));

        // Assert.
        Assert.Empty(table.OnDisk().Rows);
        await table.History.UndoAsync(TestContext.Current.CancellationToken);
        Assert.Equal(before, table.Bytes());
    }

    // ---- properties -----------------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(KnowledgeFiles.Extensions), MemberType = typeof(KnowledgeFiles))]
    public async Task AProperty_IsAddedAtTheEnd_AndBesideAnother(string extension)
    {
        // Arrange.
        await using var table = Open(extension);

        // Act.
        await table.Edit(new TableGesture("addColumn", Settings: Settings(("type", "number"))));
        await table.Edit(new TableGesture("addColumn", ViewId: "v1", TargetId: "p3", Settings: Settings(("type", "text"), ("side", "left"))));
        await table.Edit(new TableGesture("addColumn", ViewId: "v1", TargetId: "p3", Settings: Settings(("type", "checkbox"), ("side", "right"))));

        // Assert: named for their types, each with an id never used, standing where they were put.
        var disk = table.OnDisk();
        Assert.Equal(["Name", "Country", "Text", "Population", "Checkbox", "Tags", "Province", "Visited", "Founded", "Updated", "Opens", "Number"], disk.Properties.Select(property => property.Name));
        Assert.Equal(12, disk.Properties.Select(property => property.Id).Distinct().Count());
        Assert.Equal(["text", "number", "checkbox"], new[] { "Text", "Number", "Checkbox" }.Select(name => disk.Properties.Single(property => property.Name == name).ValueType));

        // The view that had an order of its own has them beside the column they were put beside.
        var text = disk.Properties.Single(property => property.Name == "Text").Id;
        var checkbox = disk.Properties.Single(property => property.Name == "Checkbox").Id;
        Assert.Equal([text, "p3", checkbox], disk.Views.Single(view => view.Id == "v1").Columns.Select(column => column.PropertyId));
    }

    [Fact]
    public async Task ARelation_IsNotAddedFromTheListOfTypes()
    {
        // Arrange.
        await using var table = Open(".yaml");

        // Act.
        (_, string answer) = table.Begin(new TableGesture("addColumn", Settings: Settings(("type", "relation"))));

        // Assert.
        Assert.Equal(KnowledgeEdits.RelationNeedsTarget, answer);
    }

    [Theory]
    [MemberData(nameof(KnowledgeFiles.Extensions), MemberType = typeof(KnowledgeFiles))]
    public async Task AProperty_IsRenamed_ButNotToANameAnotherHas_NorToNothing(string extension)
    {
        // Arrange.
        await using var table = Open(extension);

        // Act.
        await table.Edit(new TableGesture("renameColumn", ColumnId: "p3", Values: ["Inhabitants"]));
        var before = table.Bytes();
        (_, string taken) = table.Begin(new TableGesture("renameColumn", ColumnId: "p3", Values: ["country"]));
        (_, string empty) = table.Begin(new TableGesture("renameColumn", ColumnId: "p3", Values: ["  "]));

        // Assert.
        Assert.Equal("Inhabitants", table.OnDisk().Properties.Single(property => property.Id == "p3").Name);
        Assert.Equal("Another property is already called 'country'.", taken);
        Assert.Equal(KnowledgeEdits.NameNeeded, empty);
        Assert.Equal(before, table.Bytes());
    }

    [Theory]
    [MemberData(nameof(KnowledgeFiles.Extensions), MemberType = typeof(KnowledgeFiles))]
    public async Task ADeletedProperty_TakesItsCellsAndItsPlaceInEveryViewWithIt(string extension)
    {
        // Arrange: p2 has cells in both rows, and v1 filters on it and is grouped by it.
        await using var table = Open(extension);
        var before = table.Bytes();

        // Act.
        var settled = await table.Edit(new TableGesture("deleteColumn", ColumnId: "p2"));

        // Assert: nothing in the file names it any more, and nothing is reported.
        Assert.True(settled.Written);
        var body = KnowledgeDocumentStore.Read(table.Path).Body!;
        Assert.DoesNotContain(body.Table.Properties, property => property.Id == "p2");
        Assert.All(body.Table.Rows, row => Assert.DoesNotContain(row.Cells, cell => cell.PropertyId == "p2"));
        var view = body.Table.Views.Single(candidate => candidate.Id == "v1");
        Assert.Equal("", view.GroupBy);
        Assert.Empty(view.Filter.Items);
        Assert.Empty(body.Model.Findings);
        Assert.DoesNotContain(body.Model.Elements, element => element.Attributes.Values.Any(value => KnowledgeValues.Text(value) == "p2"));

        // One step.
        await table.History.UndoAsync(TestContext.Current.CancellationToken);
        Assert.Equal(before, table.Bytes());
    }

    [Fact]
    public async Task TheTitleProperty_IsNotDeleted_AndNotHidden()
    {
        // Arrange.
        await using var table = Open(".yaml");
        var before = table.Bytes();

        // Act.
        (_, string deleted) = table.Begin(new TableGesture("deleteColumn", ColumnId: "p1"));
        (_, string hidden) = table.Begin(new TableGesture("hideColumn", ColumnId: "p1", ViewId: "v1"));

        // Assert.
        Assert.Equal(KnowledgeEdits.TitleStays, deleted);
        Assert.Equal(KnowledgeEdits.TitleStays, hidden);
        Assert.Equal(before, table.Bytes());
    }

    [Theory]
    [MemberData(nameof(KnowledgeFiles.Extensions), MemberType = typeof(KnowledgeFiles))]
    public async Task ADuplicatedProperty_HasItsTypeAndItsOptions_UnderIdsOfItsOwn(string extension)
    {
        // Arrange.
        await using var table = Open(extension);

        // Act.
        await table.Edit(new TableGesture("duplicateColumn", ColumnId: "p2", ViewId: "v2"));

        // Assert: beside the first, of its type, its options copied by name and colour.
        var properties = table.OnDisk().Properties;
        var copy = properties[2];
        Assert.Equal("Country 2", copy.Name);
        Assert.Equal("selection", copy.ValueType);
        Assert.Equal(["Netherlands:blue", "Belgium:yellow"], copy.Options.Select(option => $"{option.Name}:{option.Colour}"));
        Assert.Empty(copy.Options.Select(option => option.Id).Intersect(["o1", "o2", "o3", "o4"]));
        Assert.NotEqual("p2", copy.Id);
    }

    // ---- views ----------------------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(KnowledgeFiles.Extensions), MemberType = typeof(KnowledgeFiles))]
    public async Task AView_IsAdded_Renamed_Moved_AndDeleted_ButNeverTheLast(string extension)
    {
        // Arrange.
        await using var table = Open(extension);

        // Act.
        await table.Edit(new TableGesture("addView"));
        var added = table.OnDisk().Views[2];
        await table.Edit(new TableGesture("renameView", ViewId: added.Id, Values: ["Large"]));
        await table.Edit(new TableGesture("moveView", ViewId: added.Id, Index: 0));

        // Assert.
        Assert.Equal("View", added.Name);
        Assert.Equal(["Large", "All cities", "Map"], table.OnDisk().Views.Select(view => view.Name));

        // Act: delete down to one.
        await table.Edit(new TableGesture("deleteView", ViewId: "v1"));
        await table.Edit(new TableGesture("deleteView", ViewId: "v2"));
        var before = table.Bytes();
        (_, string last) = table.Begin(new TableGesture("deleteView", ViewId: added.Id));

        // Assert: the view the file was left in is gone with its name, and the last view stays.
        var body = KnowledgeDocumentStore.Read(table.Path).Body!;
        Assert.Equal(["Large"], body.Table.Views.Select(view => view.Name));
        Assert.Equal("", body.Table.ActiveViewId);
        Assert.Empty(body.Model.Findings);
        Assert.Equal(KnowledgeEdits.LastView, last);
        Assert.Equal(before, table.Bytes());
    }

    [Theory]
    [MemberData(nameof(KnowledgeFiles.Extensions), MemberType = typeof(KnowledgeFiles))]
    public async Task ADuplicatedView_HasEverythingTheFirstHas(string extension)
    {
        // Arrange.
        await using var table = Open(extension);

        // Act.
        await table.Edit(new TableGesture("duplicateView", ViewId: "v1"));

        // Assert: beside the first, under a name and an id of its own, and otherwise the same.
        var views = table.OnDisk().Views;
        Assert.Equal(["All cities", "All cities 2", "Map"], views.Select(view => view.Name));
        (KnowledgeView first, KnowledgeView copy) = (views[0], views[1]);
        Assert.NotEqual(first.Id, copy.Id);
        Assert.Equal(first.Columns, copy.Columns);
        Assert.Equal(first.Sorts, copy.Sorts);
        Assert.Equal(first.GroupBy, copy.GroupBy);
        Assert.Equal(first.Filter.Any, copy.Filter.Any);
        Assert.Equal(first.Filter.Items, copy.Filter.Items);
    }

    [Theory]
    [MemberData(nameof(KnowledgeFiles.Extensions), MemberType = typeof(KnowledgeFiles))]
    public async Task AViewsColumns_AreHidden_Resized_Wrapped_AndMoved_AndNoOtherViewChanges(string extension)
    {
        // Arrange.
        await using var table = Open(extension);
        var other = table.OnDisk().Views.Single(view => view.Id == "v1");

        // Act: all in the view that says nothing of its columns yet.
        await table.Edit(new TableGesture("hideColumn", ColumnId: "p4", ViewId: "v2"));
        await table.Edit(new TableGesture("resizeColumn", ColumnId: "p2", ViewId: "v2", Settings: Settings(("width", "240"))));
        await table.Edit(new TableGesture("setColumnWrap", ColumnId: "p2", ViewId: "v2", Settings: Settings(("wrap", "true"))));
        await table.Edit(new TableGesture("moveColumn", ColumnId: "p3", ViewId: "v2", Index: 0));

        // Assert: p3 first, and an entry only for a column the view has something to say about.
        var disk = table.OnDisk();
        var view = disk.Views.Single(candidate => candidate.Id == "v2");
        Assert.Equal("p3", view.Columns.First(column => column.Visible).PropertyId);
        Assert.Equal(new KnowledgeColumn("p4", Visible: false, Width: 0, Wrap: false), view.Columns.Single(column => column.PropertyId == "p4"));
        Assert.Equal(new KnowledgeColumn("p2", Visible: true, Width: 240, Wrap: true), view.Columns.Single(column => column.PropertyId == "p2"));
        Assert.DoesNotContain(view.Columns, column => column.PropertyId is "p5" or "p6" or "p7" or "p8" or "p9");

        // The other view, the properties and the rows are as they were.
        Assert.Equal(other.Columns, disk.Views.Single(candidate => candidate.Id == "v1").Columns);
        Assert.Equal(["p1", "p2", "p3", "p4", "p5", "p6", "p7", "p8", "p9"], disk.Properties.Select(property => property.Id));

        // Act: shown again, unwrapped.
        await table.Edit(new TableGesture("showColumn", ColumnId: "p4", ViewId: "v2"));
        await table.Edit(new TableGesture("setColumnWrap", ColumnId: "p2", ViewId: "v2", Settings: Settings(("wrap", "false"))));

        // Assert.
        view = table.OnDisk().Views.Single(candidate => candidate.Id == "v2");
        Assert.True(view.Columns.Single(column => column.PropertyId == "p4").Visible);
        Assert.False(view.Columns.Single(column => column.PropertyId == "p2").Wrap);
    }

    [Theory]
    [MemberData(nameof(KnowledgeFiles.Extensions), MemberType = typeof(KnowledgeFiles))]
    public async Task AViewsSorts_AreAdded_Turned_Moved_AndRemoved(string extension)
    {
        // Arrange: v1 sorts by population, descending.
        await using var table = Open(extension);
        KnowledgeView View() => table.OnDisk().Views.Single(view => view.Id == "v1");

        // Act.
        await table.Edit(new TableGesture("addSort", ColumnId: "p1", ViewId: "v1", Settings: Settings(("direction", "ascending"))));
        await table.Edit(new TableGesture("setSort", ColumnId: "p3", ViewId: "v1", Settings: Settings(("direction", "ascending"))));
        await table.Edit(new TableGesture("moveSort", ColumnId: "p1", ViewId: "v1", Index: 0));

        // Assert.
        Assert.Equal([new KnowledgeSort("p1", Descending: false), new KnowledgeSort("p3", Descending: false)], View().Sorts);

        // Act.
        await table.Edit(new TableGesture("removeSort", ColumnId: "p3", ViewId: "v1"));
        await table.Edit(new TableGesture("removeSort", ColumnId: "p1", ViewId: "v1"));

        // Assert: none left, and the file still reads without a word.
        Assert.Empty(View().Sorts);
        Assert.Empty(KnowledgeDocumentStore.Read(table.Path).Body!.Model.Findings);
    }

    [Theory]
    [MemberData(nameof(KnowledgeFiles.Extensions), MemberType = typeof(KnowledgeFiles))]
    public async Task AViewsFilter_IsBuilt_Changed_AndTakenApart(string extension)
    {
        // Arrange: v1 has one condition, Country is Netherlands.
        await using var table = Open(extension);
        KnowledgeView View() => table.OnDisk().Views.Single(view => view.Id == "v1");

        // Act: a second condition, a group with a condition of its own, and any instead of all.
        await table.Edit(new TableGesture("addFilter", ColumnId: "p3", ViewId: "v1", Settings: Settings(("comparison", "greater-than"))));
        await table.Edit(new TableGesture("setFilter", ColumnId: "p3", ViewId: "v1", TargetId: "1", Values: ["500000"], Settings: Settings(("comparison", "at-least"))));
        await table.Edit(new TableGesture("addFilterGroup", ViewId: "v1"));
        await table.Edit(new TableGesture("addFilter", ColumnId: "p1", ViewId: "v1", TargetId: "2"));
        await table.Edit(new TableGesture("setFilter", ViewId: "v1", TargetId: "2/0", Values: ["Ams"], Settings: Settings(("comparison", "starts-with"))));
        await table.Edit(new TableGesture("setFilterMatch", ViewId: "v1", TargetId: "2", Settings: Settings(("match", "any"))));
        await table.Edit(new TableGesture("setFilterMatch", ViewId: "v1", Settings: Settings(("match", "any"))));

        // Assert.
        var filter = View().Filter;
        Assert.True(filter.Any);
        Assert.Equal(new KnowledgeCondition("p2", "is", "o1"), filter.Items[0]);
        Assert.Equal(new KnowledgeCondition("p3", "at-least", "500000"), filter.Items[1]);
        var group = Assert.IsType<KnowledgeFilterGroup>(filter.Items[2]);
        Assert.True(group.Any);
        Assert.Equal([new KnowledgeCondition("p1", "starts-with", "Ams")], group.Items);

        // Act: a condition for a header's menu names no comparison, and gets its type's first.
        await table.Edit(new TableGesture("addFilter", ColumnId: "p6", ViewId: "v1"));

        // Assert.
        // It is the last item, after the group, in every format.
        Assert.Equal(new KnowledgeCondition("p6", "is-checked", ""), View().Filter.Items[3]);
        Assert.IsType<KnowledgeFilterGroup>(View().Filter.Items[2]);

        // Act: taken apart from the end.
        await table.Edit(new TableGesture("removeFilter", ViewId: "v1", TargetId: "3"));
        await table.Edit(new TableGesture("removeFilter", ViewId: "v1", TargetId: "2"));
        await table.Edit(new TableGesture("removeFilter", ViewId: "v1", TargetId: "1"));
        await table.Edit(new TableGesture("removeFilter", ViewId: "v1", TargetId: "0"));

        // Assert.
        Assert.Empty(View().Filter.Items);
        Assert.Empty(KnowledgeDocumentStore.Read(table.Path).Body!.Model.Findings);
    }

    [Fact]
    public async Task AFiltersGroups_GoTwoDeep()
    {
        // Arrange.
        await using var table = Open(".yaml");
        await table.Edit(new TableGesture("addFilterGroup", ViewId: "v1"));
        await table.Edit(new TableGesture("addFilterGroup", ViewId: "v1", TargetId: "1"));

        // Act.
        (_, string answer) = table.Begin(new TableGesture("addFilterGroup", ViewId: "v1", TargetId: "1/0"));

        // Assert.
        Assert.Equal("A filter's groups go two deep.", answer);
    }

    [Theory]
    [MemberData(nameof(KnowledgeFiles.Extensions), MemberType = typeof(KnowledgeFiles))]
    public async Task AViewsGrouping_IsSet_ItsGroupsFolded_AndTakenAway(string extension)
    {
        // Arrange: v1 is grouped by Country.
        await using var table = Open(extension);
        KnowledgeView View() => table.OnDisk().Views.Single(view => view.Id == "v1");

        // Act.
        await table.Edit(new TableGesture("toggleGroup", ViewId: "v1", TargetId: "o1", Settings: Settings(("collapsed", "true"))));
        await table.Edit(new TableGesture("toggleRow", ViewId: "v1", RowId: "r1", Settings: Settings(("collapsed", "true"))));
        await table.Edit(new TableGesture("setHideEmptyGroups", ViewId: "v1", Settings: Settings(("hide", "true"))));

        // Assert.
        Assert.Equal(["o1", "r1"], View().Collapsed);
        Assert.True(View().HideEmptyGroups);

        // Act: unfolded, and then grouped by something else - which forgets the old grouping's groups.
        await table.Edit(new TableGesture("toggleGroup", ViewId: "v1", TargetId: "o1", Settings: Settings(("collapsed", "false"))));
        await table.Edit(new TableGesture("groupBy", ColumnId: "p6", ViewId: "v1"));

        // Assert.
        Assert.Equal("p6", View().GroupBy);
        Assert.Empty(View().Collapsed);

        // Act.
        (_, string refused) = table.Begin(new TableGesture("groupBy", ColumnId: "p3", ViewId: "v1"));
        await table.Edit(new TableGesture("groupBy", ColumnId: "", ViewId: "v1"));

        // Assert.
        Assert.Equal("A view is grouped by a selection, a checkbox or a relation.", refused);
        Assert.Equal("", View().GroupBy);
        Assert.Empty(KnowledgeDocumentStore.Read(table.Path).Body!.Model.Findings);
    }

    // ---- shown at once, written behind ------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(KnowledgeFiles.Extensions), MemberType = typeof(KnowledgeFiles))]
    public async Task AnEdit_IsShownBeforeItIsWritten_AndMarkedUntilItIs(string extension)
    {
        // Arrange: writes wait.
        await using var table = Open(extension);
        var before = table.Bytes();
        table.Hold();

        // Act.
        (ShortGuid edit, string answer) = table.Begin(SetCell("r2", "p1", "Antwerpen"));

        // Assert: accepted and shown, marked as not written - and the file not touched yet.
        Assert.Equal("", answer);
        var shown = table.Lines().Single(line => line.Id == "r2").Cells.Single(cell => cell.ColumnId == "p1");
        Assert.Equal(["Antwerpen"], shown.Values);
        Assert.True(shown.Pending);
        Assert.Equal(before, table.Bytes());

        // Act.
        table.Release();
        var settled = await table.Settled(edit);

        // Assert: written, and no longer marked.
        Assert.True(settled.Written);
        shown = table.Lines().Single(line => line.Id == "r2").Cells.Single(cell => cell.ColumnId == "p1");
        Assert.Equal(["Antwerpen"], shown.Values);
        Assert.False(shown.Pending);
        Assert.Equal(["Antwerpen"], Cell(table.OnDisk(), "r2", "p1"));
    }

    [Fact]
    public async Task EditsMadeWhileOneIsWaiting_AreWrittenInTheOrderTheyWereMade_EachOnTheOneBefore()
    {
        // Arrange.
        await using var table = Open(".yaml");
        table.Hold();

        // Act: a row, and a cell of that row - which exists only as an edit not yet written.
        (ShortGuid first, _) = table.Begin(new TableGesture("addRow"));
        var row = table.Lines()[2].Id;
        (ShortGuid second, string answer) = table.Begin(SetCell(row, "p1", "Ghent"));
        (ShortGuid third, _) = table.Begin(SetCell(row, "p3", "265086"));
        table.Release();
        var outcomes = new[] { await table.Settled(first), await table.Settled(second), await table.Settled(third) };

        // Assert.
        Assert.Equal("", answer);
        Assert.All(outcomes, outcome => Assert.True(outcome.Written));
        Assert.Equal(["Ghent"], Cell(table.OnDisk(), row, "p1"));
        Assert.Equal(["265086"], Cell(table.OnDisk(), row, "p3"));
    }

    [Fact]
    public async Task ARefusedWrite_TakesTheEditBack_AndEveryLaterEditWithIt()
    {
        // Arrange: the first write is refused, as a file that cannot be written refuses it.
        await using var table = Open(".yaml");
        var before = table.Bytes();
        var writes = 0;
        table.Instead = _ => ++writes == 1 ? CommandResult.Failure("cities.yaml could not be written: the disk is full.") : null;
        table.Hold();

        // Act.
        (ShortGuid first, _) = table.Begin(SetCell("r2", "p1", "Antwerpen"));
        (ShortGuid second, _) = table.Begin(SetCell("r2", "p3", "529247"));
        (ShortGuid third, _) = table.Begin(new TableGesture("addRow"));
        Assert.Equal(4, table.Lines().Count);
        table.Release();
        var settled = await table.Settled(first);

        // Assert: said once, with its reason and with what went with it; the table shows the file again.
        Assert.False(settled.Written);
        Assert.Equal("cities.yaml could not be written: the disk is full.", settled.Error);
        Assert.Equal([second, third], settled.TakenBack);
        var lines = table.Lines();
        Assert.Equal(3, lines.Count);
        Assert.Equal(["Antwerp"], lines.Single(line => line.Id == "r2").Cells.Single(cell => cell.ColumnId == "p1").Values);

        // And none of the later edits was written after all.
        await table.Session.DisposeAsync();
        Assert.Equal(1, writes);
        Assert.Equal(before, table.Bytes());
    }

    [Fact]
    public async Task ClosingATable_WaitsForTheEditsStillToBeWritten()
    {
        // Arrange.
        var table = Open(".yaml");
        table.Hold();
        table.Begin(SetCell("r2", "p1", "Antwerpen"));

        // Act.
        var closing = table.Session.DisposeAsync().AsTask();
        var closedEarly = closing.IsCompleted;
        table.Release();
        await closing;

        // Assert.
        Assert.False(closedEarly);
        Assert.Equal(["Antwerpen"], Cell(table.OnDisk(), "r2", "p1"));
    }

    /// <summary>
    /// What is shown before a write and what the file holds after it are the same table. The two
    /// are made by different code - a projection in memory, and the FBL runtime on the bytes - and
    /// this is what holds them together, for every gesture there is.
    /// </summary>
    [Theory]
    [MemberData(nameof(KnowledgeFiles.Extensions), MemberType = typeof(KnowledgeFiles))]
    public async Task WhatIsShownBeforeAWrite_IsWhatTheFileHoldsAfterIt(string extension)
    {
        // Arrange.
        await using var table = Open(extension);
        TableGesture[] gestures =
        [
            SetCell("r2", "p3", "529247"),
            SetCell("r1", "p3"),
            SetCell("r2", "p4", "o4"),
            SetCell("r1", "p4"),
            SetCell("r1", "p6", "false"),
            new("setCell", RowId: "r1", ColumnId: "p2", Settings: Settings(("newOption", "Luxembourg"))),
            new("addRow", TargetId: "r1"),
            new("addRow", ViewId: "v1", Settings: Settings(("group", "o2"))),
            new("addColumn", ViewId: "v1", TargetId: "p3", Settings: Settings(("type", "date"), ("side", "left"))),
            new("renameColumn", ColumnId: "p3", Values: ["Inhabitants"]),
            new("duplicateColumn", ColumnId: "p4", ViewId: "v1"),
            new("moveColumn", ColumnId: "p6", ViewId: "v1", Index: 1),
            new("resizeColumn", ColumnId: "p1", ViewId: "v2", Settings: Settings(("width", "300"))),
            new("hideColumn", ColumnId: "p5", ViewId: "v1"),
            new("addSort", ColumnId: "p1", ViewId: "v1"),
            new("moveSort", ColumnId: "p1", ViewId: "v1", Index: 0),
            new("addFilterGroup", ViewId: "v1"),
            new("addFilter", ColumnId: "p1", ViewId: "v1", TargetId: "1"),
            new("setFilter", ViewId: "v1", TargetId: "1/0", Values: ["A"], Settings: Settings(("comparison", "contains"))),
            new("addFilter", ColumnId: "p3", ViewId: "v1"),
            new("addFilterGroup", ViewId: "v1", TargetId: "1"),
            new("addFilter", ColumnId: "p6", ViewId: "v1", TargetId: "1"),
            new("removeFilter", ViewId: "v1", TargetId: "0"),
            new("setFilterMatch", ViewId: "v1", TargetId: "0", Settings: Settings(("match", "any"))),
            new("toggleGroup", ViewId: "v1", TargetId: "o1", Settings: Settings(("collapsed", "true"))),
            new("duplicateView", ViewId: "v1"),
            new("moveView", ViewId: "v2", Index: 0),
            new("deleteColumn", ColumnId: "p2"),
            new("deleteRows", Values: ["r2"]),
            new("deleteView", ViewId: "v1"),
        ];

        foreach (var gesture in gestures)
        {
            // Act: shown, held; then written.
            table.Hold();
            (ShortGuid edit, string answer) = table.Begin(gesture);
            Assert.Equal("", answer);
            var shown = table.Session.Shown;
            table.Release();
            var settled = await table.Settled(edit);

            // Assert.
            Assert.True(settled.Written, $"{gesture.Kind}: {settled.Error}");
            var disk = table.OnDisk();
            Assert.Equal(KnowledgeFiles.Describe(disk), KnowledgeFiles.Describe(shown));
        }
    }
}
