using System.Diagnostics;
using System.Globalization;
using System.Text;
using EtAlii.Adp.Designer.TableModel;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Designer.Knowledge.Tests;

/// <summary>
/// Ten thousand rows through the designer (knowledge-designer Requirement 9.6): opened, looked at
/// through a window, edited in one cell, sorted, filtered and grouped - with what each took
/// written to the test's output, for the record a change in the runtime is read against.
/// </summary>
/// <remarks>
/// Two things are asserted rather than timed, because a time is this machine's and these are the
/// design's: <b>an edit of one cell changes the file where that cell is and nowhere else</b>, and
/// <b>nothing the session pushes carries more lines than the window asked for</b> - not on opening,
/// not on an edit, and not when a sort moves every row there is.
/// </remarks>
public sealed class LargeTableTests : IDisposable
{
    private const int Rows = 10_000;
    private const int Window = 60;

    private readonly string _root = Directory.CreateTempSubdirectory("adp-knowledge-large-").FullName;

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

    /// <summary>A table of <paramref name="rows"/> rows by rule: a name, a number, one of three sizes and a tick on every other row.</summary>
    private static string Table(int rows)
    {
        var text = new StringBuilder();
        text.Append("ded: \"0.1\"\r\ndesigner: etalii/knowledge\r\nname: Places\r\nproperties:\r\n");
        text.Append("  - id: p1\r\n    name: Name\r\n    type: text\r\n    title: true\r\n");
        text.Append("  - id: p2\r\n    name: People\r\n    type: number\r\n");
        text.Append("  - id: p3\r\n    name: Size\r\n    type: selection\r\n    options:\r\n      - id: o1\r\n        name: Small\r\n      - id: o2\r\n        name: Medium\r\n      - id: o3\r\n        name: Large\r\n");
        text.Append("  - id: p4\r\n    name: Seen\r\n    type: checkbox\r\n");
        text.Append("views:\r\n  - id: v1\r\n    name: All\r\nrows:\r\n");
        for (var index = 0; index < rows; index++)
        {
            text.Append(CultureInfo.InvariantCulture, $"  - id: r{index}\r\n    cells:\r\n");
            text.Append(CultureInfo.InvariantCulture, $"      - property: p1\r\n        text: Place {index}\r\n");
            text.Append(CultureInfo.InvariantCulture, $"      - property: p2\r\n        number: {(index * 7919) % 100_003}\r\n");
            text.Append(CultureInfo.InvariantCulture, $"      - property: p3\r\n        option: o{1 + (index % 3)}\r\n");
            if (index % 2 == 0)
            {
                text.Append("      - property: p4\r\n        checked: true\r\n");
            }
        }

        return text.ToString();
    }

    [Fact]
    public async Task TenThousandRows_AreOpenedThroughAWindow_AndEditedWhereTheEditIs()
    {
        // Arrange.
        var path = IoPath.Combine(_root, "places.yaml");
        await File.WriteAllTextAsync(path, Table(Rows), TestContext.Current.CancellationToken);
        var before = await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken);
        var output = TestContext.Current.TestOutputHelper;
        var watch = Stopwatch.StartNew();
        void Took(string what)
        {
            output?.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{what}: {watch.ElapsedMilliseconds} ms"));
            watch.Restart();
        }

        // Act: opened.
        await using var table = new EditingTable(path, TimeSpan.FromMinutes(5));
        List<TableChange> pushed = [];
        table.Session.Changed += (_, args) =>
        {
            lock (pushed)
            {
                pushed.AddRange(args.Changes);
            }
        };
        var baseline = table.Session.Baseline();
        Took($"opened {before.Length / 1024} KiB");

        // Assert: every row counted, with the line a row is added at, and none of them sent yet.
        Assert.Equal(Rows + 1, baseline.RowCount);
        Assert.Empty(baseline.Findings);

        // Act: a window in the middle.
        table.Session.SetWindow(5000, Window);
        Took("a window of the plain view");

        // Assert.
        TableRowsChanged LastRows()
        {
            lock (pushed)
            {
                return pushed.OfType<TableRowsChanged>().Last();
            }
        }

        Assert.Equal(Window, LastRows().Rows.Count);
        Assert.Equal("r5000", LastRows().Rows[0].Id);

        // Act: one cell, in the middle of the file.
        await table.Edit(new TableGesture("setCell", RowId: "r5000", ColumnId: "p1", Values: ["Somewhere else"]));
        Took("one cell edited and written");

        // Assert: the file is the file it was but for that cell - everything before it and everything after it, byte for byte.
        var after = await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken);
        var same = 0;
        while (same < before.Length && same < after.Length && before[same] == after[same])
        {
            same++;
        }

        var tail = 0;
        while (tail < before.Length - same && tail < after.Length - same && before[^(tail + 1)] == after[^(tail + 1)])
        {
            tail++;
        }

        Assert.InRange(before.Length - same - tail, 1, "Place 5000".Length);
        Assert.InRange(after.Length - same - tail, 1, "Somewhere else".Length);
        Assert.Contains("text: Somewhere else", Encoding.UTF8.GetString(after), StringComparison.Ordinal);

        // Act: the view sorted, filtered and grouped - each moves or hides thousands of rows.
        await table.Edit(new TableGesture("addSort", ColumnId: "p2", ViewId: "v1", Settings: new Dictionary<string, string> { ["direction"] = "descending" }));
        Took("sorted by number, descending");
        var sorted = LastRows();
        await table.Edit(new TableGesture("addFilter", ColumnId: "p4", ViewId: "v1"));
        Took("filtered to the ticked rows");
        var filtered = LastRows();
        await table.Edit(new TableGesture("groupBy", ColumnId: "p3", ViewId: "v1"));
        Took("grouped by size");
        var grouped = LastRows();
        table.Session.SetWindow(0, Window);
        Took("a window of the grouped view");

        // Assert: the window follows the view - sorted by number as a number, then half the rows, then under headings.
        var numbers = sorted.Rows.Select(row => double.Parse(row.Cells.Single(cell => cell.ColumnId == "p2").Values[0], CultureInfo.InvariantCulture)).ToList();
        Assert.Equal(numbers.OrderDescending(), numbers);
        Assert.Equal(Rows + 1, sorted.RowCount);
        Assert.Equal((Rows / 2) + 1, filtered.RowCount);
        Assert.All(filtered.Rows.Where(row => !row.IsNewRow), row => Assert.Contains(row.Cells, cell => cell.ColumnId == "p4"));
        Assert.Equal((Rows / 2) + 4 + 4, grouped.RowCount);
        var first = LastRows();
        Assert.True(first.Rows[0].IsGroup);
        Assert.Equal("Small", first.Rows[0].Label);

        // And nothing that was pushed, at any point, carried more lines than the window asked for.
        lock (pushed)
        {
            var windows = pushed.OfType<TableRowsChanged>().ToList();
            Assert.True(windows.Count >= 6, $"Only {windows.Count} windows were pushed.");
            Assert.All(windows, window => Assert.InRange(window.Rows.Count, 1, Window));
        }
    }
}
