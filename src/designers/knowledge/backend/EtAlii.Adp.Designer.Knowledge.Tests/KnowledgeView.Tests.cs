using System.Globalization;
using EtAlii.Adp.Designer.TableModel;
using Xunit;

namespace EtAlii.Adp.Designer.Knowledge.Tests;

/// <summary>
/// A view's lines (knowledge-designer Requirements 6.4 to 6.7): which rows its filter lets through,
/// in which order its sorts put them, and under which headings its grouping shows them.
/// </summary>
/// <remarks>
/// <b>Properties of the result rather than examples alone.</b> A filter is checked against every
/// row of a table of some hundreds, both ways: every row shown passes it and every row not shown
/// does not, by a reading of the comparison written out here rather than by the engine's own. A
/// sort is checked to be an order and to be stable; a grouping to lose no row and to count what is
/// beneath each heading.
/// </remarks>
public sealed class KnowledgeViewTests
{
    private static readonly IReadOnlySet<string> None = new HashSet<string>();
    private static readonly IReadOnlySet<(string, string)> NoCells = new HashSet<(string, string)>();

    private static readonly KnowledgeOption[] Sizes = [new("small", "Small", "default"), new("medium", "Medium", "default"), new("large", "Large", "default")];
    private static readonly KnowledgeOption[] Tags = [new("port", "Port", "default"), new("capital", "Capital", "default"), new("old", "Old", "default")];

    private static KnowledgeProperty Property(string id, string type, KnowledgeOption[]? options = null, bool parent = false) =>
        new(id, id, type, IsTitle: id == "name", options ?? [], parent ? "." : "", parent ? "one" : "none", "", IsComputed: false, IsParent: parent);

    private static readonly KnowledgeProperty[] Properties =
    [
        Property("name", "text"),
        Property("people", "number"),
        Property("seen", "checkbox"),
        Property("founded", "date"),
        Property("size", "selection", Sizes),
        Property("tags", "multipleSelection", Tags),
        Property("under", "relation", parent: true),
    ];

    /// <summary>
    /// A table of rows made by rule, not by chance: the same every run, and with every kind of
    /// hole - a row without a number, without a size, without tags - so that an empty value is
    /// met by every comparison.
    /// </summary>
    private static KnowledgeTable Table(int count = 240)
    {
        List<KnowledgeRow> rows = [];
        for (var index = 0; index < count; index++)
        {
            List<KnowledgeCell> cells = [new("name", [$"{"abcdefgh"[index % 8]}{(index * 7) % 13} city {index}"], "text")];
            if (index % 5 != 0)
            {
                cells.Add(new KnowledgeCell("people", [((index * 37) % 101 * (index % 3 == 0 ? 1 : 10)).ToString(CultureInfo.InvariantCulture)], "number"));
            }

            if (index % 2 == 0)
            {
                cells.Add(new KnowledgeCell("seen", ["true"], "checked"));
            }

            if (index % 7 != 0)
            {
                cells.Add(new KnowledgeCell("founded", [new DateOnly(1200 + ((index * 53) % 800), 1 + (index % 12), 1 + (index % 28)).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)], "date"));
            }

            if (index % 4 != 0)
            {
                cells.Add(new KnowledgeCell("size", [Sizes[index % 3].Id], "option"));
            }

            if (index % 3 != 0)
            {
                cells.Add(new KnowledgeCell("tags", index % 6 == 1 ? [Tags[0].Id, Tags[2].Id] : [Tags[index % 3].Id], "options"));
            }

            // A value of a type the property had before: text where a number is expected.
            if (index % 41 == 0)
            {
                cells.RemoveAll(cell => cell.PropertyId == "people");
                cells.Add(new KnowledgeCell("people", ["a great many"], "text"));
            }

            rows.Add(new KnowledgeRow($"r{index}", cells));
        }

        return new KnowledgeTable("Cities", "v", Properties, [], rows);
    }

    private static KnowledgeView View(KnowledgeFilterGroup? filter = null, KnowledgeSort[]? sorts = null, string groupBy = "", bool hideEmpty = false, string[]? order = null, string[]? hidden = null, string[]? collapsed = null) =>
        new("v", "View", [], sorts ?? [], filter ?? new KnowledgeFilterGroup(false, []), groupBy, hideEmpty, order ?? [], hidden ?? [], collapsed ?? []);

    private static List<TableRow> Lines(KnowledgeTable table, KnowledgeView view, bool editable = false, IReadOnlySet<string>? kept = null) =>
        KnowledgeViewEngine.Lines(table, view, kept ?? None, NoCells, editable);

    private static string? One(KnowledgeRow row, string propertyId, string key) =>
        row.Cells.FirstOrDefault(cell => cell.PropertyId == propertyId && cell.Key == key)?.Values.FirstOrDefault();

    private static double? Number(KnowledgeRow row) => One(row, "people", "number") is { } text ? double.Parse(text, CultureInfo.InvariantCulture) : null;

    // ---- the filter -----------------------------------------------------------------------------------

    /// <summary>Each comparison with a value to compare with, and what it means - written here, apart from the engine.</summary>
    public static TheoryData<string, string, string> Comparisons() => new()
    {
        { "name", "is", "A0 CITY 0" },
        { "name", "is-not", "a0 city 0" },
        { "name", "contains", "CITY 1" },
        { "name", "does-not-contain", "city 1" },
        { "name", "starts-with", "B" },
        { "name", "ends-with", "7" },
        { "name", "is-empty", "" },
        { "name", "is-not-empty", "" },
        { "people", "equals", "370" },
        { "people", "does-not-equal", "370" },
        { "people", "greater-than", "99" },
        { "people", "less-than", "100" },
        { "people", "at-least", "100" },
        { "people", "at-most", "99" },
        { "people", "is-empty", "" },
        { "people", "is-not-empty", "" },
        { "seen", "is-checked", "" },
        { "seen", "is-not-checked", "" },
        { "founded", "is", "1253-02-02" },
        { "founded", "is-before", "1500-01-01" },
        { "founded", "is-after", "1500-01-01" },
        { "founded", "is-on-or-before", "1253-02-02" },
        { "founded", "is-on-or-after", "1253-02-02" },
        { "founded", "is-empty", "" },
        { "size", "is", "medium" },
        { "size", "is-not", "medium" },
        { "size", "is-empty", "" },
        { "tags", "contains", "old" },
        { "tags", "does-not-contain", "old" },
        { "tags", "is-not-empty", "" },
    };

    /// <summary>What a comparison means, by the row's own values. Not the engine's code: the check on it.</summary>
    private static bool Means(KnowledgeRow row, string propertyId, string comparison, string given)
    {
        var name = One(row, "name", "text") ?? "";
        var people = Number(row);
        var founded = One(row, "founded", "date");
        var size = One(row, "size", "option");
        var tags = row.Cells.FirstOrDefault(cell => cell.PropertyId == "tags")?.Values ?? [];
        var has = propertyId switch
        {
            "name" => name.Length > 0,
            "people" => people is not null,
            "seen" => One(row, "seen", "checked") is not null,
            "founded" => founded is not null,
            "size" => size is not null,
            _ => tags.Count > 0,
        };
        return (propertyId, comparison) switch
        {
            (_, "is-empty") => !has,
            (_, "is-not-empty") => has,
            ("name", "is") => name.Equals(given, StringComparison.OrdinalIgnoreCase),
            ("name", "is-not") => !name.Equals(given, StringComparison.OrdinalIgnoreCase),
            ("name", "contains") => name.Contains(given, StringComparison.OrdinalIgnoreCase),
            ("name", "does-not-contain") => !name.Contains(given, StringComparison.OrdinalIgnoreCase),
            ("name", "starts-with") => name.StartsWith(given, StringComparison.OrdinalIgnoreCase),
            ("name", "ends-with") => name.EndsWith(given, StringComparison.OrdinalIgnoreCase),
            ("people", "equals") => Nullable.Equals(people, double.Parse(given, CultureInfo.InvariantCulture)),
            ("people", "does-not-equal") => !Nullable.Equals(people, double.Parse(given, CultureInfo.InvariantCulture)),
            ("people", "greater-than") => people > double.Parse(given, CultureInfo.InvariantCulture),
            ("people", "less-than") => people < double.Parse(given, CultureInfo.InvariantCulture),
            ("people", "at-least") => people >= double.Parse(given, CultureInfo.InvariantCulture),
            ("people", "at-most") => people <= double.Parse(given, CultureInfo.InvariantCulture),
            ("seen", "is-checked") => has,
            ("seen", "is-not-checked") => !has,
            ("founded", "is") => founded == given,
            ("founded", "is-before") => founded is not null && string.CompareOrdinal(founded, given) < 0,
            ("founded", "is-after") => founded is not null && string.CompareOrdinal(founded, given) > 0,
            ("founded", "is-on-or-before") => founded is not null && string.CompareOrdinal(founded, given) <= 0,
            ("founded", "is-on-or-after") => founded is not null && string.CompareOrdinal(founded, given) >= 0,
            ("size", "is") => size == given,
            ("size", "is-not") => size != given,
            ("tags", "contains") => tags.Contains(given),
            ("tags", "does-not-contain") => !tags.Contains(given),
            _ => throw new InvalidOperationException($"No meaning written for {propertyId} {comparison}."),
        };
    }

    [Theory]
    [MemberData(nameof(Comparisons))]
    public void EveryRowShown_PassesTheCondition_AndEveryRowHidden_DoesNot(string propertyId, string comparison, string given)
    {
        // Arrange.
        var table = Table();

        // Act.
        var shown = Lines(table, View(new KnowledgeFilterGroup(false, [new KnowledgeCondition(propertyId, comparison, given)]))).Select(line => line.Id).ToHashSet();

        // Assert: both ways, and the case is not empty on either side - a filter that shows all or nothing proves little.
        Assert.All(table.Rows, row => Assert.Equal(Means(row, propertyId, comparison, given), shown.Contains(row.Id)));
        if (propertyId != "name" || comparison is not ("is-empty" or "is-not-empty" or "is-not"))
        {
            Assert.InRange(shown.Count, 1, table.Rows.Count - 1);
        }
    }

    [Fact]
    public void EveryComparisonTheDefinitionHas_IsOneTheEngineKnows()
    {
        // Arrange: a value each type can be compared with.
        var table = Table();
        var samples = new Dictionary<string, (string PropertyId, string Given)>
        {
            ["text"] = ("name", "a0 city 0"),
            ["number"] = ("people", "100"),
            ["checkbox"] = ("seen", ""),
            ["date"] = ("founded", "1500-01-01"),
            ["selection"] = ("size", "medium"),
            ["multipleSelection"] = ("tags", "old"),
        };

        foreach ((string type, (string propertyId, string given)) in samples)
        {
            var comparisons = KnowledgeVocabulary.Comparisons[type];
            Assert.NotEmpty(comparisons);
            foreach (var comparison in comparisons)
            {
                // Act.
                var shown = Lines(table, View(new KnowledgeFilterGroup(false, [new KnowledgeCondition(propertyId, comparison, given)])));

                // Assert: a comparison the engine did not know would let every row through.
                Assert.True(shown.Count < table.Rows.Count, $"{type} {comparison} filtered nothing.");
            }
        }
    }

    [Fact]
    public void ConditionsAreJoinedByAllOrByAny_AndAGroupIsOneItem()
    {
        // Arrange.
        var table = Table();
        var large = new KnowledgeCondition("size", "is", "large");
        var seen = new KnowledgeCondition("seen", "is-checked", "");
        var many = new KnowledgeCondition("people", "at-least", "500");

        // Act.
        var all = Shown(new KnowledgeFilterGroup(false, [large, seen]));
        var any = Shown(new KnowledgeFilterGroup(true, [large, seen]));
        var nested = Shown(new KnowledgeFilterGroup(false, [large, new KnowledgeFilterGroup(true, [seen, many])]));

        // Assert.
        Assert.All(table.Rows, row => Assert.Equal(Is(row, large) && Is(row, seen), all.Contains(row.Id)));
        Assert.All(table.Rows, row => Assert.Equal(Is(row, large) || Is(row, seen), any.Contains(row.Id)));
        Assert.All(table.Rows, row => Assert.Equal(Is(row, large) && (Is(row, seen) || Is(row, many)), nested.Contains(row.Id)));
        Assert.True(all.Count < nested.Count && nested.Count < any.Count);
        return;

        bool Is(KnowledgeRow row, KnowledgeCondition condition) => Means(row, condition.PropertyId, condition.Operator, condition.Value);

        HashSet<string> Shown(KnowledgeFilterGroup filter) => [.. Lines(table, View(filter)).Select(line => line.Id)];
    }

    [Fact]
    public void AConditionThatIsNotFinished_FiltersNothing_AndARowAddedHereStaysInSight()
    {
        // Arrange.
        var table = Table(30);

        // Act: a comparison waiting for its value, and a filter that hides a row which was just added.
        var unfinished = Lines(table, View(new KnowledgeFilterGroup(false, [new KnowledgeCondition("people", "greater-than", "")])));
        var hidden = Lines(table, View(new KnowledgeFilterGroup(false, [new KnowledgeCondition("name", "is", "nothing is called this")])));
        var kept = Lines(table, View(new KnowledgeFilterGroup(false, [new KnowledgeCondition("name", "is", "nothing is called this")])), kept: new HashSet<string> { "r7" });

        // Assert.
        Assert.Equal(30, unfinished.Count);
        Assert.Empty(hidden);
        Assert.Equal(["r7"], kept.Select(line => line.Id));
    }

    // ---- the sorts ------------------------------------------------------------------------------------

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ASortByNumber_IsByNumber_WithTheRowsWithoutOneLast_AndIsStable(bool descending)
    {
        // Arrange.
        var table = Table();
        var place = table.Rows.Select((row, index) => (row.Id, index)).ToDictionary(pair => pair.Id, pair => pair.index);

        // Act.
        var lines = Lines(table, View(sorts: [new KnowledgeSort("people", descending)]));

        // Assert: every row once.
        Assert.Equal(table.Rows.Count, lines.Select(line => line.Id).Distinct().Count());
        var rows = lines.Select(line => table.Rows[place[line.Id]]).ToList();
        for (var index = 1; index < rows.Count; index++)
        {
            (double? before, double? after) = (Number(rows[index - 1]), Number(rows[index]));

            // A row without a number is after every row with one, whichever way the sort runs - and
            // that includes the row holding a text where the number should be.
            Assert.False(before is null && after is not null, $"{rows[index - 1].Id} has no number and stands before {rows[index].Id}, which has.");
            if (before is not null && after is not null)
            {
                // As numbers: 9 before 10, which as text it would not be.
                Assert.True(descending ? before >= after : before <= after, $"{before} stands before {after}.");
            }

            // Stable: rows the sort does not tell apart keep the file's order.
            if (Nullable.Equals(before, after))
            {
                Assert.True(place[rows[index - 1].Id] < place[rows[index].Id], $"{rows[index - 1].Id} and {rows[index].Id} changed places.");
            }
        }

        Assert.Contains(rows, row => Number(row) is null);
    }

    [Fact]
    public void SeveralSorts_ApplyInTurn_TheFirstDecidingFirst()
    {
        // Arrange.
        var table = Table();
        var byId = table.Rows.ToDictionary(row => row.Id);

        // Act: by size - in the options' order, not by their names - and within a size by number, descending.
        var rows = Lines(table, View(sorts: [new KnowledgeSort("size", false), new KnowledgeSort("people", true)])).Select(line => byId[line.Id]).ToList();

        // Assert.
        for (var index = 1; index < rows.Count; index++)
        {
            Assert.True(Size(rows[index - 1]) <= Size(rows[index]), "The first sort did not decide first.");
            if (Size(rows[index - 1]) == Size(rows[index]) && Number(rows[index - 1]) is { } before && Number(rows[index]) is { } after)
            {
                Assert.True(before >= after, "Within one size the second sort did not decide.");
            }
        }

        // Small, Medium, Large is the options' order; by name it would be Large, Medium, Small.
        Assert.Equal(["small", "medium", "large"], rows.Select(row => One(row, "size", "option")).OfType<string>().Distinct());
        return;

        int Size(KnowledgeRow row) => One(row, "size", "option") is { } id ? Array.FindIndex(Sizes, option => option.Id == id) : int.MaxValue;
    }

    [Fact]
    public void ASort_IsOfTheRowsTheFilterLetsThrough()
    {
        // Arrange.
        var table = Table();

        // Act.
        var lines = Lines(table, View(new KnowledgeFilterGroup(false, [new KnowledgeCondition("seen", "is-checked", "")]), [new KnowledgeSort("name", false)]));

        // Assert.
        Assert.Equal(120, lines.Count);
        var names = lines.Select(line => line.Cells.Single(cell => cell.ColumnId == "name").Values[0]).ToList();
        Assert.Equal(names.Order(StringComparer.OrdinalIgnoreCase), names);
    }

    // ---- groups ---------------------------------------------------------------------------------------

    [Fact]
    public void AGrouping_ShowsEveryRowUnderItsValue_WithAHeadingForTheRowsWithout_AndCountsWhatIsBeneath()
    {
        // Arrange.
        var table = Table();

        // Act.
        var lines = Lines(table, View(groupBy: "size"));

        // Assert: the options' order, then the rows without a size.
        var headings = lines.Where(line => line.IsGroup).ToList();
        Assert.Equal(["small", "medium", "large", KnowledgeViewEngine.NoValue], headings.Select(heading => heading.Id));
        Assert.Equal(["Small", "Medium", "Large", "No size"], headings.Select(heading => heading.Label));

        // Every row once, under the heading of its value, and each heading counting the rows beneath it.
        var beneath = Beneath(lines);
        Assert.Equal(table.Rows.Count, beneath.Values.Sum(rows => rows.Count));
        foreach (var heading in headings)
        {
            Assert.Equal(heading.Count, beneath[heading.Id].Count);
            Assert.All(beneath[heading.Id], id => Assert.Equal(heading.Id, One(table.Rows.Single(row => row.Id == id), "size", "option") ?? KnowledgeViewEngine.NoValue));
        }

        Assert.NotEmpty(beneath[KnowledgeViewEngine.NoValue]);
    }

    [Fact]
    public void ARowWithSeveralValues_IsUnderEachOfThem()
    {
        // Arrange.
        var table = Table();
        var both = table.Rows.Where(row => row.Cells.Any(cell => cell is { PropertyId: "tags", Values.Count: 2 })).Select(row => row.Id).ToList();

        // Act.
        var beneath = Beneath(Lines(table, View(groupBy: "tags")));

        // Assert: under Port and under Old, and so counted twice - and the rows without a tag once, under their own heading.
        Assert.NotEmpty(both);
        Assert.All(both, id => Assert.True(beneath["port"].Contains(id) && beneath["old"].Contains(id)));
        Assert.Equal(table.Rows.Count + both.Count, beneath.Values.Sum(rows => rows.Count));
        Assert.Equal(table.Rows.Count(row => row.Cells.All(cell => cell.PropertyId != "tags")), beneath[KnowledgeViewEngine.NoValue].Count);
    }

    [Fact]
    public void ACheckboxGroups_AsUntickedAndTicked_WithNoHeadingForNoValue()
    {
        // Act.
        var lines = Lines(Table(), View(groupBy: "seen"));

        // Assert.
        var headings = lines.Where(line => line.IsGroup).ToList();
        Assert.Equal(["false", "true"], headings.Select(heading => heading.Id));
        Assert.Equal([120, 120], headings.Select(heading => heading.Count));
    }

    [Fact]
    public void AViewOrdersHidesAndFoldsItsGroups_AndLeavesOutTheEmptyOnesWhenAsked()
    {
        // Arrange: no row is large.
        var table = Table() with { Rows = [.. Table().Rows.Where(row => One(row, "size", "option") != "large")] };

        // Act.
        var plain = Lines(table, View(groupBy: "size"), editable: true);
        var arranged = Lines(table, View(groupBy: "size", hideEmpty: true, order: [KnowledgeViewEngine.NoValue, "medium"], hidden: ["small"], collapsed: ["medium"]), editable: true);

        // Assert: an empty group is shown with nothing beneath it but the line to add a row at.
        Assert.Equal(0, plain.Single(line => line is { IsGroup: true, Id: "large" }).Count);
        Assert.Contains(plain, line => line is { IsNewRow: true, Id: "large" });

        // The view's order first, the hidden group and the empty one gone, the folded one a heading with its count and nothing beneath.
        Assert.Equal([KnowledgeViewEngine.NoValue, "medium"], arranged.Where(line => line.IsGroup).Select(line => line.Id));
        var medium = arranged.Single(line => line is { IsGroup: true, Id: "medium" });
        Assert.True(medium.Collapsed);
        Assert.True(medium.Count > 0);
        Assert.Same(medium, arranged[^1]);
        Assert.DoesNotContain(arranged, line => line is { IsNewRow: true, Id: "medium" });
    }

    [Fact]
    public void RowsAreSortedWithinTheirGroups()
    {
        // Act.
        var lines = Lines(Table(), View(sorts: [new KnowledgeSort("people", true)], groupBy: "size"));

        // Assert.
        foreach (var rows in Beneath(lines).Values)
        {
            var numbers = rows.Select(id => lines.First(line => line.Id == id && !line.IsGroup).Cells.FirstOrDefault(cell => cell.ColumnId == "people")?.Values[0])
                .Where(text => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
                .Select(text => double.Parse(text!, CultureInfo.InvariantCulture))
                .ToList();
            Assert.Equal(numbers.OrderDescending(), numbers);
        }
    }

    /// <summary>The ids of the rows beneath each heading, by the heading's key.</summary>
    private static Dictionary<string, List<string>> Beneath(List<TableRow> lines)
    {
        Dictionary<string, List<string>> beneath = [];
        var current = "";
        foreach (var line in lines)
        {
            if (line.IsGroup)
            {
                current = line.Id;
                beneath[current] = [];
            }
            else if (!line.IsNewRow)
            {
                beneath[current].Add(line.Id);
            }
        }

        return beneath;
    }

    // ---- nesting --------------------------------------------------------------------------------------

    private static KnowledgeTable Family(params (string Id, string Under)[] rows) => new(
        "Places",
        "v",
        Properties,
        [],
        [.. rows.Select(row => new KnowledgeRow(row.Id, row.Under.Length > 0 ? [new KnowledgeCell("name", [row.Id], "text"), new KnowledgeCell("under", [row.Under], "rows")] : [new KnowledgeCell("name", [row.Id], "text")]))]);

    [Fact]
    public void GroupedByTheParentRelation_RowsAreNestedUnderTheirParents_ToAnyDepth()
    {
        // Arrange: a country, its provinces, their cities - listed out of order.
        var table = Family(("leiden", "south"), ("nl", ""), ("south", "nl"), ("north", "nl"), ("be", ""), ("haarlem", "north"), ("delft", "south"));

        // Act.
        var lines = Lines(table, View(groupBy: "under"));

        // Assert: each under its parent, a level deeper, and no heading in sight.
        Assert.Equal(
            ["nl:0", "south:1", "leiden:2", "delft:2", "north:1", "haarlem:2", "be:0"],
            lines.Select(line => $"{line.Id}:{line.Depth}"));
        Assert.DoesNotContain(lines, line => line.IsGroup);
        Assert.Equal(["nl", "south", "north"], lines.Where(line => line.HasChildren).Select(line => line.Id));
    }

    [Fact]
    public void AFoldedParent_HidesEverythingUnderIt_AndARowWhoseParentIsNotShown_StandsAtTheTop()
    {
        // Arrange.
        var table = Family(("nl", ""), ("south", "nl"), ("leiden", "south"), ("orphan", "nowhere"), ("be", ""));

        // Act.
        var lines = Lines(table, View(groupBy: "under", collapsed: ["nl"]));

        // Assert.
        Assert.Equal(["nl:0", "orphan:0", "be:0"], lines.Select(line => $"{line.Id}:{line.Depth}"));
        Assert.True(lines[0].Collapsed);
    }

    [Fact]
    public void RowsThatAreEachOthersParents_AreShownOnce_AndNoneIsLost()
    {
        // Arrange: a circle, which a file written outside ADP can hold.
        var table = Family(("a", "b"), ("b", "c"), ("c", "a"), ("self", "self"), ("free", ""));

        // Act.
        var lines = Lines(table, View(groupBy: "under"));

        // Assert.
        Assert.Equal(["a", "b", "c", "free", "self"], lines.Select(line => line.Id).Order(StringComparer.Ordinal));
    }

    // ---- the lines a row is added at ------------------------------------------------------------------------

    [Fact]
    public void AnEditableView_EndsWithTheLineARowIsAddedAt_AndAViewThatIsNot_DoesNot()
    {
        // Arrange.
        var table = Table(3);

        // Act.
        var editable = Lines(table, View(), editable: true);
        var shown = Lines(table, View());

        // Assert.
        Assert.Equal(4, editable.Count);
        Assert.True(editable[^1].IsNewRow);
        Assert.Equal("", editable[^1].Id);
        Assert.Equal(3, shown.Count);
        Assert.DoesNotContain(shown, line => line.IsNewRow);
    }
}
