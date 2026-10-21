using EtAlii.Adp.Designer.TableModel;
using Xunit;

namespace EtAlii.Adp.Designer.Knowledge.Tests;

/// <summary>
/// What one connection looks at in a knowledge table (FR-088): the view it has open, the rows an
/// edit added that stay in sight until the view is opened again, what it searches for, and the
/// window of lines it is sent - never below zero, never past the end, and none before it asks.
/// </summary>
public sealed class KnowledgeViewStateTests
{
    private static readonly IReadOnlySet<(string, string)> NoCells = new HashSet<(string, string)>();

    /// <summary>Three cities, a view that shows only those starting with an A, and a view that shows all.</summary>
    private static readonly KnowledgeTable Cities = new(
        "Cities",
        "onlyA",
        [new KnowledgeProperty("name", "Name", "text", IsTitle: true, [], "", "none", "", IsComputed: false, IsParent: false)],
        [View("onlyA", new KnowledgeCondition("name", "starts-with", "A")), View("all")],
        [Row("r1", "Amsterdam"), Row("r2", "Brussels"), Row("r3", "Antwerp")]);

    private static KnowledgeView View(string id, params KnowledgeFilterItem[] conditions) =>
        new(id, id, [], [], new KnowledgeFilterGroup(false, conditions), "", false, [], [], []);

    private static KnowledgeRow Row(string id, string name) => new(id, [new KnowledgeCell("name", [name], "text")]);

    private static readonly Func<string, string, string> NoLabel = (_, _) => "";

    private static List<string> InSight(KnowledgeViewState state) => [.. state.InSight(Cities, NoLabel).Select(row => row.Id)];

    private static List<TableRow> Lines(int count) => [.. Enumerable.Range(0, count).Select(index => new TableRow($"r{index}"))];

    [Fact]
    public void TheViewOpenedWith_IsTheViewShown()
    {
        // Arrange.
        var state = new KnowledgeViewState("all");

        // Act.
        var lines = state.Lines(Cities, NoCells, editable: false, NoLabel);

        // Assert.
        Assert.Equal("all", state.ViewId);
        Assert.Equal(["r1", "r2", "r3"], lines.Select(line => line.Id));
    }

    [Fact]
    public void ARowKeptInSight_StaysThere_UntilAViewIsOpenedAgain()
    {
        // Arrange: Brussels is not let through by the view's filter.
        var state = new KnowledgeViewState("onlyA");
        Assert.Equal(["r1", "r3"], InSight(state));

        // Act and assert: kept, it is in sight...
        state.Keep("r2");
        Assert.Equal(["r1", "r2", "r3"], InSight(state));

        // ...until a view is opened, even the same one.
        state.Open("onlyA");
        Assert.Equal(["r1", "r3"], InSight(state));
    }

    [Fact]
    public void OpeningAnotherView_ShowsThatView()
    {
        // Arrange.
        var state = new KnowledgeViewState("onlyA");

        // Act.
        state.Open("all");

        // Assert.
        Assert.Equal("all", state.ViewId);
        Assert.Equal(["r1", "r2", "r3"], InSight(state));
    }

    [Fact]
    public void ASearch_IsKeptWithoutTheSpacesAroundIt_AndFindsOnlyTheRowsWithIt()
    {
        // Arrange.
        var state = new KnowledgeViewState("all");

        // Act.
        state.SearchFor("  werp ");

        // Assert.
        Assert.Equal("werp", state.Search);
        Assert.Equal(["r3"], InSight(state));

        // And an empty search is all of them.
        state.SearchFor("");
        Assert.Equal(["r1", "r2", "r3"], InSight(state));
    }

    [Fact]
    public void WithoutAWindow_NoLinesAreSent_AndNoneAreMade()
    {
        // Arrange.
        var state = new KnowledgeViewState("all");
        var made = false;

        // Act.
        var rows = state.RowsInWindow(() =>
        {
            made = true;
            return Lines(3);
        });

        // Assert.
        Assert.Null(rows);
        Assert.False(made);
    }

    [Fact]
    public void TheWindow_IsTheLinesAskedFor()
    {
        // Arrange.
        var state = new KnowledgeViewState("all");

        // Act.
        state.SetWindow(1, 2);
        var rows = state.RowsInWindow(() => Lines(5));

        // Assert.
        Assert.NotNull(rows);
        Assert.Equal(1, rows.First);
        Assert.Equal(["r1", "r2"], rows.Rows.Select(row => row.Id));
        Assert.Equal(5, rows.RowCount);
    }

    [Fact]
    public void AWindowBelowZero_StartsAtTheTop_AndIsEmpty()
    {
        // Arrange.
        var state = new KnowledgeViewState("all");

        // Act.
        state.SetWindow(-4, -1);
        var rows = state.RowsInWindow(() => Lines(3));

        // Assert.
        Assert.NotNull(rows);
        Assert.Equal(0, rows.First);
        Assert.Empty(rows.Rows);
        Assert.Equal(3, rows.RowCount);
    }

    [Fact]
    public void AWindowPastTheEnd_IsEmpty_AtTheEnd()
    {
        // Arrange.
        var state = new KnowledgeViewState("all");

        // Act.
        state.SetWindow(10, 5);
        var rows = state.RowsInWindow(() => Lines(3));

        // Assert.
        Assert.NotNull(rows);
        Assert.Equal(3, rows.First);
        Assert.Empty(rows.Rows);
        Assert.Equal(3, rows.RowCount);
    }
}
