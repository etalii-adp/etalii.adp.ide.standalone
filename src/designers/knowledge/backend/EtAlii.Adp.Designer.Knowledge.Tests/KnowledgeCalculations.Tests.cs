using System.Globalization;
using EtAlii.Adp.Designer.TableModel;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Designer.Knowledge.Tests;

/// <summary>
/// The three things a view does beside filtering, sorting and grouping, as Notion's tables do them:
/// the line under a column that calculates over the rows in sight, the columns frozen up to one,
/// and the search - which is the connection's own and is written nowhere.
/// </summary>
public sealed class KnowledgeCalculationsTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("adp-knowledge-calculations-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    /// <summary>The example in its second view, which shows every row.</summary>
    private EditingTable Open(string extension) => new(KnowledgeFiles.CopyPlain("cities" + extension, _root));

    private static Dictionary<string, string> Settings(params (string Name, string Value)[] settings) => settings.ToDictionary(setting => setting.Name, setting => setting.Value);

    private static TableGesture Calculate(string columnId, string calculation) => new("setColumnCalculation", ColumnId: columnId, Settings: Settings(("calculate", calculation)));

    private static TableColumn Column(EditingTable table, string id) => table.Session.Baseline().Columns.Single(column => column.Id == id);

    private static string Shown(EditingTable table, string id) => Column(table, id).Settings.GetValueOrDefault("calculation") ?? "(none)";

    private static KnowledgeView View(EditingTable table) => table.OnDisk().Views.Single(view => view.Id == "v2");

    /// <summary>The numbers the rows hold for a property, read from the file.</summary>
    private static List<double> Numbers(EditingTable table, string propertyId) =>
        [.. table.OnDisk().Rows.SelectMany(row => row.Cells).Where(cell => cell.PropertyId == propertyId).SelectMany(cell => cell.Values).Select(value => double.Parse(value, CultureInfo.InvariantCulture)).Order()];

    private static string Figure(double value) => Math.Round(value, 10).ToString("0.##########", CultureInfo.InvariantCulture);

    [Theory]
    [MemberData(nameof(KnowledgeFiles.Extensions), MemberType = typeof(KnowledgeFiles))]
    public async Task ACalculation_LeavesItsColumnWhereItWas(string extension)
    {
        // Arrange: a view that lists none of its columns shows them in the file's order. A setting
        // given to one used to list it on its own, after the columns the view listed and before
        // every other: the column jumped to the left, and its figure stood where another had been.
        await using var table = Open(extension);
        await table.Edit(new TableGesture("resizeColumn", ColumnId: "p3", ViewId: "v2", Settings: Settings(("width", "120"))));
        var before = table.Session.Baseline().Columns.Select(column => column.Id).ToList();

        // Act: a calculation and a width for columns the view does not list yet, further along.
        await table.Edit(new TableGesture("setColumnCalculation", ColumnId: "p6", ViewId: "v2", Settings: Settings(("calculate", "countAll"))));
        await table.Edit(new TableGesture("resizeColumn", ColumnId: "p8", ViewId: "v2", Settings: Settings(("width", "200"))));

        // Assert: the columns are shown in the order they were, and the figure is the column's own.
        Assert.Equal(before, table.Session.Baseline().Columns.Select(column => column.Id));
        Assert.NotEqual("(none)", Shown(table, "p6"));
    }

    [Theory]
    [MemberData(nameof(KnowledgeFiles.Extensions), MemberType = typeof(KnowledgeFiles))]
    public async Task AColumn_ShowsUnderItWhatItsCalculationComesTo_AndTheFileNamesIt(string extension)
    {
        // Arrange: nothing is calculated, and the population says which calculations it can have. Two more
        // cities are given one, the second the same as another has, so a count of values is not a count of different ones.
        await using var table = Open(extension);
        await table.Edit(new TableGesture("setCell", RowId: "r2", ColumnId: "p3", Values: ["1000.5"]));
        await table.Edit(new TableGesture("setCell", RowId: "a8q7ddqb6zyubcyf5vuky6iwb", ColumnId: "p3", Values: ["345345"]));
        var rows = table.OnDisk().Rows.Count;
        var numbers = Numbers(table, "p3");
        Assert.True(numbers.Count >= 3 && numbers.Count < rows && numbers.Distinct().Count() < numbers.Count, "The example needs populations, and rows without one, for this to tell a count from a count of values.");
        Assert.Equal("(none)", Shown(table, "p3"));
        Assert.Contains("median", Column(table, "p3").Settings["calculations"].Split(','));
        Assert.DoesNotContain("median", Column(table, "p1").Settings["calculations"].Split(','));

        // Act and assert: each figure against the numbers read from the file.
        Dictionary<string, string> expected = new()
        {
            ["countAll"] = rows.ToString(CultureInfo.InvariantCulture),
            ["countValues"] = numbers.Count.ToString(CultureInfo.InvariantCulture),
            ["countUnique"] = numbers.Distinct().Count().ToString(CultureInfo.InvariantCulture),
            ["countEmpty"] = (rows - numbers.Count).ToString(CultureInfo.InvariantCulture),
            ["countNotEmpty"] = numbers.Count.ToString(CultureInfo.InvariantCulture),
            ["percentEmpty"] = Figure(Math.Round(100.0 * (rows - numbers.Count) / rows, 1)) + "%",
            ["percentNotEmpty"] = Figure(Math.Round(100.0 * numbers.Count / rows, 1)) + "%",
            ["sum"] = Figure(numbers.Sum()),
            ["average"] = Figure(numbers.Average()),
            ["median"] = Figure(numbers.Count % 2 == 1 ? numbers[numbers.Count / 2] : (numbers[(numbers.Count / 2) - 1] + numbers[numbers.Count / 2]) / 2),
            ["min"] = Figure(numbers[0]),
            ["max"] = Figure(numbers[^1]),
            ["range"] = Figure(numbers[^1] - numbers[0]),
        };
        foreach ((string calculation, string figure) in expected)
        {
            await table.Edit(Calculate("p3", calculation));
            Assert.Equal((calculation, figure), (calculation, Shown(table, "p3")));
            Assert.Equal(calculation, View(table).Columns.Single(column => column.PropertyId == "p3").Calculate);
        }

        // The file says it in its own words: the name with a hyphen, as the definition writes it.
        Assert.Contains("range", await File.ReadAllTextAsync(table.Path, TestContext.Current.CancellationToken), StringComparison.Ordinal);
        await table.Edit(Calculate("p3", "countUnique"));
        Assert.Contains("count-unique", await File.ReadAllTextAsync(table.Path, TestContext.Current.CancellationToken), StringComparison.Ordinal);

        // Act: taken away again.
        await table.Edit(Calculate("p3", ""));

        // Assert: the line shows nothing and the file says nothing.
        Assert.Equal("(none)", Shown(table, "p3"));
        Assert.DoesNotContain("count-unique", await File.ReadAllTextAsync(table.Path, TestContext.Current.CancellationToken), StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(KnowledgeFiles.Extensions), MemberType = typeof(KnowledgeFiles))]
    public async Task Dates_ShowTheirEnds_ABoxCountsAsFilledWhenTicked_AndACalculationOfAnotherTypeIsRefused(string extension)
    {
        // Arrange: two more cities are given the day they were founded.
        await using var table = Open(extension);
        await table.Edit(new TableGesture("setCell", RowId: "r2", ColumnId: "p7", Values: ["1291-02-21"]));
        await table.Edit(new TableGesture("setCell", RowId: "a8q7ddqb6zyubcyf5vuky6iwb", ColumnId: "p7", Values: ["1343-09-01"]));
        List<string> founded = [.. table.OnDisk().Rows.SelectMany(row => row.Cells).Where(cell => cell.PropertyId == "p7").SelectMany(cell => cell.Values).Order(StringComparer.Ordinal)];
        Assert.True(founded.Count >= 2, "The example needs two dates for a range.");
        var ticked = table.OnDisk().Rows.Count(row => row.Cells.Any(cell => cell.PropertyId == "p6" && cell.Values.Contains("true")));

        // Act and assert: the ends as their author wrote them, and the days between.
        await table.Edit(Calculate("p7", "earliest"));
        Assert.Equal(founded[0], Shown(table, "p7"));
        await table.Edit(Calculate("p7", "latest"));
        Assert.Equal(founded[^1], Shown(table, "p7"));
        await table.Edit(Calculate("p7", "dateRange"));
        var days = DateOnly.ParseExact(founded[^1], "yyyy-MM-dd", CultureInfo.InvariantCulture).DayNumber - DateOnly.ParseExact(founded[0], "yyyy-MM-dd", CultureInfo.InvariantCulture).DayNumber;
        Assert.Equal($"{days} day{(days == 1 ? "" : "s")}", Shown(table, "p7"));

        // A box that is not ticked is empty, whether the file says false or says nothing.
        await table.Edit(Calculate("p6", "countNotEmpty"));
        Assert.Equal(ticked.ToString(CultureInfo.InvariantCulture), Shown(table, "p6"));

        // Act: a sum of names, and a calculation nobody knows.
        var bytes = table.Bytes();
        (_, string sum) = table.Begin(Calculate("p1", "sum"));
        (_, string unknown) = table.Begin(Calculate("p1", "guess"));

        // Assert: both refused, saying why, and nothing written.
        Assert.Contains("Name", sum, StringComparison.Ordinal);
        Assert.Equal("That is not a calculation.", unknown);
        Assert.Equal(bytes, table.Bytes());
    }

    [Theory]
    [MemberData(nameof(KnowledgeFiles.Extensions), MemberType = typeof(KnowledgeFiles))]
    public async Task ACalculation_IsOverTheRowsInSight_AndOneLeftFromAnotherTypeShowsNothingAndIsKept(string extension)
    {
        // Arrange: the sum of every population, a Belgian city's among them.
        await using var table = Open(extension);
        await table.Edit(new TableGesture("setCell", RowId: "r2", ColumnId: "p3", Values: ["530504"]));
        await table.Edit(Calculate("p3", "sum"));
        var all = Shown(table, "p3");

        // Act: the view is filtered to the cities of one country.
        await table.Edit(new TableGesture("addFilter", ColumnId: "p2"));
        await table.Edit(new TableGesture("setFilter", TargetId: "0", Values: ["o1"], Settings: Settings(("comparison", "is"))));

        // Assert: the sum is of those rows alone.
        var dutch = table.OnDisk().Rows
            .Where(row => row.Cells.Any(cell => cell.PropertyId == "p2" && cell.Values.Contains("o1")))
            .SelectMany(row => row.Cells.Where(cell => cell.PropertyId == "p3").SelectMany(cell => cell.Values))
            .Sum(value => double.Parse(value, CultureInfo.InvariantCulture));
        Assert.Equal(Figure(dutch), Shown(table, "p3"));
        Assert.NotEqual(all, Shown(table, "p3"));

        // Act: the population becomes text.
        await table.Edit(new TableGesture("setColumnType", ColumnId: "p3", Settings: Settings(("type", "text"), ("confirmed", "true"))));

        // Assert: a sum of text shows nothing, and the view still says what it was asked for.
        Assert.Equal("", Shown(table, "p3"));
        Assert.Equal("sum", View(table).Columns.Single(column => column.PropertyId == "p3").Calculate);
    }

    [Theory]
    [MemberData(nameof(KnowledgeFiles.Extensions), MemberType = typeof(KnowledgeFiles))]
    public async Task AViewIsFrozenUpToAColumn_ACopyOfItIsToo_AndDeletingThatColumnUnfreezesBoth(string extension)
    {
        // Arrange.
        await using var table = Open(extension);
        Assert.False(table.Session.Baseline().Settings.Settings.ContainsKey("freeze"));

        // Act: frozen up to the country, with a calculation under the population.
        await table.Edit(new TableGesture("freezeColumns", ColumnId: "p2"));
        await table.Edit(Calculate("p3", "max"));

        // Assert: the view says so, to the file and to the screen; saying it again changes nothing.
        Assert.Equal("p2", View(table).Freeze);
        Assert.Equal("p2", table.Session.Baseline().Settings.Settings["freeze"]);
        var bytes = table.Bytes();
        (_, string again) = table.Begin(new TableGesture("freezeColumns", ColumnId: "p2"));
        Assert.Equal("", again);
        Assert.Equal(bytes, table.Bytes());

        // Act: the view is copied.
        await table.Edit(new TableGesture("duplicateView", ViewId: "v2"));

        // Assert: the copy is frozen as far, and calculates the same.
        var copy = table.OnDisk().Views.Single(view => view.Id is not ("v1" or "v2"));
        Assert.Equal("p2", copy.Freeze);
        Assert.Equal("max", copy.Columns.Single(column => column.PropertyId == "p3").Calculate);

        // Act: the country goes.
        await table.Edit(new TableGesture("deleteColumn", ColumnId: "p2"));

        // Assert: neither view is frozen up to a column that is not there, and the table reports nothing of it.
        Assert.All(table.OnDisk().Views, view => Assert.Equal("", view.Freeze));
        Assert.DoesNotContain(table.Session.Baseline().Findings, finding => finding.Code == KnowledgeValidator.ViewNamesMissingProperty);

        // Act: frozen up to the population, then not at all.
        await table.Edit(new TableGesture("freezeColumns", ViewId: "v2", ColumnId: "p3"));
        await table.Edit(new TableGesture("freezeColumns", ViewId: "v2"));

        // Assert.
        Assert.Equal("", View(table).Freeze);
        (_, string gone) = table.Begin(new TableGesture("freezeColumns", ViewId: "v2", ColumnId: "p2"));
        Assert.NotEqual("", gone);
    }

    [Fact]
    public async Task AFreezeNamingAPropertyTheTableDoesNotHave_IsReported_AndTheViewOpensWithoutIt()
    {
        // Arrange: a file frozen, by hand, up to a property that is not there.
        var path = KnowledgeFiles.CopyPlain("cities.yaml", _root);
        var text = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);
        var frozen = text.Replace("    name: Map", "    name: Map\n    freeze: p99", StringComparison.Ordinal);
        Assert.NotEqual(text, frozen);
        await File.WriteAllTextAsync(path, frozen, TestContext.Current.CancellationToken);

        // Act.
        await using var table = new EditingTable(path);
        var baseline = table.Session.Baseline();

        // Assert.
        Assert.Contains(baseline.Findings, finding => finding.Code == KnowledgeValidator.ViewNamesMissingProperty && finding.Message.Contains("p99", StringComparison.Ordinal));
        Assert.False(baseline.Settings.Settings.ContainsKey("freeze"));
        Assert.Equal("p99", View(table).Freeze);
    }

    [Theory]
    [MemberData(nameof(KnowledgeFiles.Extensions), MemberType = typeof(KnowledgeFiles))]
    public async Task ASearch_ShowsTheRowsThatMentionIt_ByNameOptionOrRelatedRow_AndWritesNothing(string extension)
    {
        // Arrange.
        await using var table = Open(extension);
        var all = table.Lines().Where(line => line.Id.Length > 0).Select(line => line.Id).ToList();
        var onDisk = table.OnDisk();
        var first = onDisk.Rows[0];
        var name = first.Cells.Single(cell => cell.PropertyId == "p1").Values[0];
        await table.Edit(Calculate("p1", "countAll"));
        var bytes = table.Bytes();

        // Act: by a part of a name, whatever its case.
        await table.Edit(new TableGesture("search", Values: [name[1..].ToUpperInvariant()]));

        // Assert: the rows whose name has it, the count under the name is of them, and the view says what is searched for.
        var named = onDisk.Rows.Where(row => row.Cells.Any(cell => cell.PropertyId != "p6" && cell.Key == "text" && cell.Values.Any(value => value.Contains(name[1..], StringComparison.CurrentCultureIgnoreCase)))).Select(row => row.Id).ToList();
        Assert.Contains(first.Id, Found());
        Assert.Equal(named, Found());
        Assert.True(named.Count < all.Count, "A search that finds every row tells nothing.");
        Assert.Equal(named.Count.ToString(CultureInfo.InvariantCulture), Shown(table, "p1"));
        Assert.Equal(name[1..].ToUpperInvariant(), table.Session.Baseline().Settings.Settings["search"]);

        // Act: by the name of an option, which the rows hold by its id.
        await table.Edit(new TableGesture("search", Values: ["Belgium"]));

        // Assert.
        var belgian = onDisk.Rows.Where(row => row.Cells.Any(cell => cell.PropertyId == "p2" && cell.Values.Contains("o2"))).Select(row => row.Id).ToList();
        Assert.NotEmpty(belgian);
        Assert.Equal(belgian, Found().Intersect(belgian));
        Assert.DoesNotContain("o2", Found());

        // Act: by the title of a related row, which the rows hold by its id too.
        var provinces = KnowledgeDocumentStore.Read(IoPath.Combine(_root, KnowledgeFiles.Related)).Body!.Table;
        var related = onDisk.Rows.SelectMany(row => row.Cells).First(cell => cell is { PropertyId: "p5", Values.Count: > 0 }).Values[0];
        var title = KnowledgeRelations.TitleOf(provinces, provinces.Rows.Single(row => row.Id == related));
        await table.Edit(new TableGesture("search", Values: [title]));

        // Assert.
        var inProvince = onDisk.Rows.Where(row => row.Cells.Any(cell => cell.PropertyId == "p5" && cell.Values.Contains(related))).Select(row => row.Id).ToList();
        Assert.Equal(inProvince, Found().Intersect(inProvince));

        // Act: for what no row mentions, then for nothing.
        await table.Edit(new TableGesture("search", Values: ["no city is called this"]));
        Assert.Empty(Found());
        await table.Edit(new TableGesture("search", Values: [""]));

        // Assert: every row again, and through all of it the file was never written.
        Assert.Equal(all, Found());
        Assert.False(table.Session.Baseline().Settings.Settings.ContainsKey("search"));
        Assert.Equal(bytes, table.Bytes());
        return;

        List<string> Found() => [.. table.Lines().Where(line => line.Id.Length > 0).Select(line => line.Id)];
    }
}
