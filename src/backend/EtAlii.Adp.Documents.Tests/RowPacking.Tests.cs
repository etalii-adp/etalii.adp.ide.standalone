using Xunit;

namespace EtAlii.Adp.Documents.Tests;

/// <summary>The arrangement every row canvas offers: fewest rows, nothing overlapping, linked items close.</summary>
public class RowPackingTests
{
    [Fact]
    public void OverlappingItems_TakeAsManyRowsAsOverlapAtOnePoint_AndNoMore()
    {
        // Arrange: at x = 15 three items overlap, and nowhere do four.
        RowItem[] items =
        [
            new("a", 0, 20), new("b", 10, 30), new("c", 12, 18), new("d", 25, 40), new("e", 35, 50), new("f", 45, 60),
        ];

        // Act.
        var rows = RowPacking.Pack(items, [], gap: 0);

        // Assert.
        Assert.Equal(3, rows.Values.Distinct().Count());
        AssertNoOverlap(items, rows, gap: 0);
    }

    [Fact]
    public void TheGap_KeepsTwoItemsThatWouldTouch_OnSeparateRows()
    {
        // Act.
        var rows = RowPacking.Pack([new RowItem("a", 0, 10), new RowItem("b", 15, 20)], [], gap: 8);

        // Assert.
        Assert.NotEqual(rows["a"], rows["b"]);
    }

    [Fact]
    public void ALinkedChain_RunsAlongOneRow_EvenWhenAnotherRowIsFreeFirst()
    {
        // Arrange: x and y overlap, so there are two rows; a then b then c follow each other and are
        // linked, so they belong on one row - whichever row a took.
        RowItem[] items = [new("x", 0, 10), new("a", 0, 10), new("b", 20, 30), new("c", 40, 50), new("y", 20, 30)];

        // Act.
        var rows = RowPacking.Pack(items, [("a", "b"), ("b", "c")], gap: 4);

        // Assert.
        Assert.Equal(rows["a"], rows["b"]);
        Assert.Equal(rows["b"], rows["c"]);
        AssertNoOverlap(items, rows, gap: 4);
    }

    [Fact]
    public void APairKeptApart_TakesTheNeighbouringRow_NotTheSameOne()
    {
        // Arrange: m then a, linked and following each other, so they would share a row - but the
        // link would run through m's label, so they are kept apart. Two other items open two rows.
        RowItem[] items = [new("m", 0, 10), new("x", 0, 10), new("y", 0, 10), new("a", 20, 30)];

        // Act.
        var rows = RowPacking.Pack(items, [("m", "a")], gap: 4, apart: [("m", "a")]);

        // Assert.
        Assert.Equal(1, Math.Abs(rows["m"] - rows["a"]));
    }

    [Fact]
    public void RowsSwap_SoLinkedItemsEndUpOnNeighbouringRows()
    {
        // Arrange: three stacked items at the start; a later item linked to the one packed lowest.
        RowItem[] items = [new("p", 0, 10), new("q", 0, 10), new("r", 0, 10), new("later", 0, 10)];

        // Act: "later" overlaps all three and opens a fourth row; it is linked to "p", on row 0.
        var rows = RowPacking.Pack(items, [("p", "later")], gap: 0);

        // Assert.
        Assert.Equal(1, Math.Abs(rows["p"] - rows["later"]));
        AssertNoOverlap(items, rows, gap: 0);
    }

    [Fact]
    public void ATallItem_KeepsEveryRowItCovers_Clear()
    {
        // Arrange: a two-row note, and two items under it in time.
        RowItem[] items = [new("note", 0, 50, Rows: 2), new("a", 10, 20), new("b", 30, 40)];

        // Act.
        var rows = RowPacking.Pack(items, [], gap: 0);

        // Assert.
        AssertNoOverlap(items, rows, gap: 0);
    }

    [Fact]
    public void PackingAPackedArrangement_ChangesNothing()
    {
        // Arrange.
        RowItem[] items = [new("a", 0, 20), new("b", 10, 30), new("c", 25, 40), new("d", 5, 8), new("e", 45, 60)];
        (string, string)[] links = [("a", "c"), ("b", "e")];
        var first = RowPacking.Pack(items, links, gap: 2);

        // Act: the same items again, now listed by their rows.
        var again = RowPacking.Pack([.. items.OrderBy(item => first[item.Id])], links, gap: 2);

        // Assert.
        Assert.Equal(first.OrderBy(entry => entry.Key), again.OrderBy(entry => entry.Key));
    }

    private static void AssertNoOverlap(IReadOnlyList<RowItem> items, IReadOnlyDictionary<string, int> rows, double gap)
    {
        for (var i = 0; i < items.Count; i++)
        {
            for (var j = i + 1; j < items.Count; j++)
            {
                var a = items[i];
                var b = items[j];
                var sharesARow = rows[a.Id] < rows[b.Id] + b.Rows && rows[b.Id] < rows[a.Id] + a.Rows;
                var apart = a.Right + gap <= b.Left || b.Right + gap <= a.Left;
                Assert.True(!sharesARow || apart, $"{a.Id} and {b.Id} overlap on a row.");
            }
        }
    }
}
