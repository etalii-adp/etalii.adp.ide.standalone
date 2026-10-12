using EtAlii.Adp.Designer.TableModel;
using Xunit;

namespace EtAlii.Adp.Designer.Knowledge.Tests;

/// <summary>
/// What an author does to single groups of a grouped view (knowledge-designer Requirement 6.6):
/// hide one, show the hidden ones again, and move one up or down - each stored in the view, in
/// every format, and each what the table shows from then on.
/// </summary>
public sealed class KnowledgeGroupsTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("adp-knowledge-groups-").FullName;

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

    /// <summary>The example: its first view is grouped by the country its cities are in.</summary>
    private EditingTable Grouped(string extension)
    {
        var table = new EditingTable(KnowledgeFiles.CopyGrouped("cities" + extension, _root));
        Assert.Equal("p2", table.OnDisk().Views.Single(view => view.Id == "v1").GroupBy);
        return table;
    }

    private static List<string> Headings(EditingTable table) => [.. table.Lines().Where(line => line.IsGroup).Select(line => line.Id)];

    private static TableGesture Move(string key, string direction) =>
        new("moveGroup", ViewId: "v1", TargetId: key, Settings: new Dictionary<string, string> { ["direction"] = direction });

    [Theory]
    [MemberData(nameof(KnowledgeFiles.Extensions), MemberType = typeof(KnowledgeFiles))]
    public async Task AGroup_IsMovedOnePlaceAmongThoseShown_AndTheViewNamesEveryGroupFromThenOn(string extension)
    {
        // Arrange: at least three groups, in the order the options and the rows without a country give them.
        await using var table = Grouped(extension);
        var before = Headings(table);
        Assert.True(before.Count >= 3, $"The example has fewer than three groups to order: {string.Join(", ", before)}.");
        Assert.Empty(table.OnDisk().Views.Single(view => view.Id == "v1").GroupOrder);

        // Act: the second group goes up.
        await table.Edit(Move(before[1], "up"));

        // Assert: it changed places with the first, on the screen and in the file, and nothing else moved.
        List<string> expected = [before[1], before[0], .. before.Skip(2)];
        Assert.Equal(expected, Headings(table));
        Assert.Equal(expected, table.OnDisk().Views.Single(view => view.Id == "v1").GroupOrder);

        // Act: and down again; then the last one down, which has nowhere to go.
        await table.Edit(Move(before[1], "down"));
        var bytes = table.Bytes();
        (_, string answer) = table.Begin(Move(before[^1], "down"));

        // Assert.
        Assert.Equal(before, Headings(table));
        Assert.Equal("", answer);
        Assert.Equal(bytes, table.Bytes());
    }

    [Theory]
    [MemberData(nameof(KnowledgeFiles.Extensions), MemberType = typeof(KnowledgeFiles))]
    public async Task AGroup_IsHidden_SaidToBeHidden_SteppedOverByAMove_AndShownAgain(string extension)
    {
        // Arrange.
        await using var table = Grouped(extension);
        var before = Headings(table);
        Assert.DoesNotContain("hiddenGroups", table.Session.Baseline().Settings.Settings.Keys);

        // Act: the second group is hidden.
        await table.Edit(new TableGesture("hideGroup", ViewId: "v1", TargetId: before[1]));

        // Assert: gone from the screen, in the file, and counted for the control that shows it again.
        Assert.Equal([before[0], .. before.Skip(2)], Headings(table));
        Assert.Equal([before[1]], table.OnDisk().Views.Single(view => view.Id == "v1").HiddenGroups);
        Assert.Equal("1", table.Session.Baseline().Settings.Settings["hiddenGroups"]);

        // Act: the third group goes up: over the hidden one, to the place before the first.
        await table.Edit(Move(before[2], "up"));

        // Assert.
        Assert.Equal([before[2], before[0], .. before.Skip(3)], Headings(table));

        // Act: every hidden group is shown again.
        await table.Edit(new TableGesture("showGroups", ViewId: "v1"));

        // Assert: none is hidden, and the one that was stands where the order has it.
        Assert.Empty(table.OnDisk().Views.Single(view => view.Id == "v1").HiddenGroups);
        Assert.DoesNotContain("hiddenGroups", table.Session.Baseline().Settings.Settings.Keys);
        Assert.Equal(before.Order(StringComparer.Ordinal), Headings(table).Order(StringComparer.Ordinal));
        Assert.Equal(before[2], Headings(table)[0]);
    }

    /// <summary>The groups the view says it has, as the group menu reads them: key, name and whether it is hidden.</summary>
    private static List<(string Key, string Label, bool Hidden)> Listed(EditingTable table) =>
        [.. System.Text.Json.JsonDocument.Parse(table.Session.Baseline().Settings.Settings["groups"]).RootElement.EnumerateArray().Select(group => (group.GetProperty("key").GetString()!, group.GetProperty("label").GetString()!, group.GetProperty("hidden").GetBoolean()))];

    [Theory]
    [MemberData(nameof(KnowledgeFiles.Extensions), MemberType = typeof(KnowledgeFiles))]
    public async Task TheViewListsItsGroups_HiddenOnesToo_AndOneOfThemIsShownAgainByItself(string extension)
    {
        // Arrange: two groups hidden.
        await using var table = Grouped(extension);
        var before = Headings(table);
        Assert.Equal(before, Listed(table).Select(group => group.Key));
        Assert.Equal(("o1", "Netherlands", false), Listed(table)[0]);
        Assert.Equal((KnowledgeViewEngine.NoValue, "No Country"), (Listed(table)[^1].Key, Listed(table)[^1].Label));
        await table.Edit(new TableGesture("hideGroup", ViewId: "v1", TargetId: before[0]));
        await table.Edit(new TableGesture("hideGroup", ViewId: "v1", TargetId: before[1]));

        // Assert: both are still listed, as hidden, where they stand.
        Assert.Equal(before, Listed(table).Select(group => group.Key));
        Assert.Equal([true, true], Listed(table).Take(2).Select(group => group.Hidden));

        // Act: the second one is shown again.
        await table.Edit(new TableGesture("showGroup", ViewId: "v1", TargetId: before[1]));

        // Assert: it alone.
        Assert.Equal([before[0]], table.OnDisk().Views.Single(view => view.Id == "v1").HiddenGroups);
        Assert.Equal(before.Skip(1), Headings(table));
    }

    [Theory]
    [MemberData(nameof(KnowledgeFiles.Extensions), MemberType = typeof(KnowledgeFiles))]
    public async Task Groups_ArePutInAPlaceAmongAllOfThem_AndInTheOrderOfTheirNames_WithTheRowsWithoutAValueLast(string extension)
    {
        // Arrange.
        await using var table = Grouped(extension);
        var before = Headings(table);
        var names = Listed(table).ToDictionary(group => group.Key, group => group.Label);

        // Act: the first group is dropped at the third place.
        await table.Edit(new TableGesture("moveGroup", ViewId: "v1", TargetId: before[0], Index: 2));

        // Assert.
        List<string> moved = [before[1], before[2], before[0], .. before.Skip(3)];
        Assert.Equal(moved, Headings(table));
        Assert.Equal(moved, table.OnDisk().Views.Single(view => view.Id == "v1").GroupOrder);

        // Act: by name, and by name backwards.
        await table.Edit(new TableGesture("sortGroups", ViewId: "v1", Settings: new Dictionary<string, string> { ["order"] = "ascending" }));
        var ascending = Headings(table);
        await table.Edit(new TableGesture("sortGroups", ViewId: "v1", Settings: new Dictionary<string, string> { ["order"] = "descending" }));
        var descending = Headings(table);

        // Assert: the named groups in the order of their names, and the rows without a country last both times.
        List<string> named = [.. before.Where(key => key != KnowledgeViewEngine.NoValue).OrderBy(key => names[key], StringComparer.CurrentCultureIgnoreCase)];
        Assert.Equal([.. named, KnowledgeViewEngine.NoValue], ascending);
        Assert.Equal([.. named.AsEnumerable().Reverse(), KnowledgeViewEngine.NoValue], descending);
        Assert.Equal(descending, table.OnDisk().Views.Single(view => view.Id == "v1").GroupOrder);
    }

    [Theory]
    [MemberData(nameof(KnowledgeFiles.Extensions), MemberType = typeof(KnowledgeFiles))]
    public async Task Rows_AreMovedTogetherBesideAnotherRow_InAViewWithoutASort_AndNotInOneWithASort(string extension)
    {
        // Arrange: the example's second view has no sort; four rows are added so that there is something to move.
        await using var table = new EditingTable(KnowledgeFiles.CopyPlain("cities" + extension, _root));
        Assert.Empty(table.OnDisk().Views.Single(view => view.Id == "v2").Sorts);
        for (var added = 0; added < 2; added++)
        {
            await table.Edit(new TableGesture("addRow", ViewId: "v2"));
        }

        List<string> before = [.. table.OnDisk().Rows.Select(row => row.Id)];
        Assert.True(before.Count >= 4, "The example has fewer than four rows to move.");

        // Act: the first and the third row go after the last, together.
        await table.Edit(new TableGesture("moveRows", ViewId: "v2", Values: [before[2], before[0]], TargetId: before[^1], Settings: new Dictionary<string, string> { ["place"] = "after" }));

        // Assert: in the file's order, which is the order they were in.
        List<string> expected = [.. before.Where(id => id != before[0] && id != before[2]), before[0], before[2]];
        Assert.Equal(expected, table.OnDisk().Rows.Select(row => row.Id));

        // Act: one of them back before the first.
        await table.Edit(new TableGesture("moveRows", ViewId: "v2", RowId: before[0], TargetId: expected[0]));

        // Assert.
        Assert.Equal([before[0], .. expected.Where(id => id != before[0])], table.OnDisk().Rows.Select(row => row.Id));

        // Act: in the view that sorts, nothing moves.
        var bytes = table.Bytes();
        (_, string answer) = table.Begin(new TableGesture("moveRows", ViewId: "v1", RowId: before[0], TargetId: before[^1]));

        // Assert.
        Assert.Equal(KnowledgeEdits.SortedRowsStay, answer);
        Assert.Equal(bytes, table.Bytes());
    }
}
