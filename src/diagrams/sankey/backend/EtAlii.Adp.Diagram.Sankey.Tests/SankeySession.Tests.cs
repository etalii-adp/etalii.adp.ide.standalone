using EtAlii.Adp.Documents;
using EtAlii.Adp.History;
using Xunit;

namespace EtAlii.Adp.Diagram.Sankey.Tests;

/// <summary>
/// A session over the company example: what it delivers, and that dragging a node up or down its
/// column moves its entry - and nothing else.
/// </summary>
public sealed class SankeySessionTests : IDisposable
{
    private readonly SankeyTestFolder _folder = new(SankeyExamples.BrightwaterCoffee, "brightwater-coffee.skv");
    private readonly SankeyDocumentStore _store = new();

    public void Dispose() => _folder.Dispose();

    [Fact]
    public async Task AFreshSession_DeliversEveryNodeAndFlow_NodesFirst()
    {
        // Arrange.
        var model = Parse();
        await using var session = new SankeySession(_folder.Body, _store, new SankeyElementMapper());

        // Act.
        var delivered = Delivered(session.Baseline());

        // Assert.
        Assert.Equal(model.Nodes.Select(node => node.Id).Concat(model.Flows.Select(flow => flow.Id)), delivered.Select(element => element.Id));
        Assert.All(delivered.Take(model.Nodes.Count), element => Assert.Equal(SankeyElementMapper.NodeType, element.Type));
        Assert.All(delivered.Skip(model.Nodes.Count), element => Assert.Equal(SankeyElementMapper.FlowType, element.Type));
    }

    [Fact]
    public async Task ANodeArrives_WithItsValueInItsFormat_AndItsLabelsOnTheRightSide()
    {
        // Arrange.
        await using var session = new SankeySession(_folder.Body, _store, new SankeyElementMapper());

        // Act.
        var delivered = Delivered(session.Baseline());

        // Assert.
        var cafes = Node(delivered, "cafes");
        Assert.Equal(("Cafés", 1840d, "€1,840M", "+9% Y/Y", "blue", SankeyElementMapper.LeftSide), (cafes.Name, cafes.Value, cafes.DisplayValue, cafes.Note, cafes.Color, cafes.Side));
        var tax = Node(delivered, "tax");
        Assert.Equal(("(€140M)", "red", SankeyElementMapper.RightSide, 4), (tax.DisplayValue, tax.Color, tax.Side, tax.Column));
        Assert.Equal(3500, Node(delivered, "revenue").Value);
    }

    [Fact]
    public async Task AFlow_TakesItsTargetsColour_AndIsAsThickAsItsValue()
    {
        // Arrange.
        await using var session = new SankeySession(_folder.Body, _store, new SankeyElementMapper());
        var delivered = Delivered(session.Baseline());

        // Act.
        var cost = Flow(delivered, "revenue->cost-of-revenue");
        var profit = Flow(delivered, "revenue->gross-profit");

        // Assert.
        Assert.Equal(("red", "green"), (cost.Color, profit.Color));
        Assert.Equal(1890d / 1610d, cost.Thickness / profit.Thickness, 2);
        Assert.Equal(Node(delivered, "revenue").Height, cost.Thickness + profit.Thickness, 1);
    }

    [Fact]
    public async Task DraggingANodeBelowItsNeighbour_MovesItsEntryAfterIt()
    {
        // Arrange.
        using var history = new HistoryStack(new SankeyTestDispatcher(_store));
        await using var session = new SankeySession(_folder.Body, _store, new SankeyElementMapper(), history);
        var layout = SankeyLayout.Of(_store.GetOrLoad(_folder.Body).Model);
        var cafes = layout.Boxes["cafes"];
        var packaged = layout.Boxes["packaged"];

        // Act: the cafés dropped with their middle just below the packaged coffee's.
        var refusal = await session.MoveElementToAsync("cafes", cafes.X + 40, packaged.CentreY + 1 - (cafes.Height / 2), TestContext.Current.CancellationToken);

        // Assert: the order changed, the column did not.
        Assert.Equal("", refusal);
        var after = SankeyLayout.Of(Parse());
        Assert.Equal(["packaged", "cafes", "wholesale", "licensing"], after.ColumnOrder[0]);
        Assert.Equal(cafes.X, after.Boxes["cafes"].X);
    }

    [Fact]
    public async Task DroppingANodeBackInItsPlace_WritesNothing()
    {
        // Arrange.
        var original = await File.ReadAllBytesAsync(_folder.Body, TestContext.Current.CancellationToken);
        using var history = new HistoryStack(new SankeyTestDispatcher(_store));
        await using var session = new SankeySession(_folder.Body, _store, new SankeyElementMapper(), history);
        var box = SankeyLayout.Of(_store.GetOrLoad(_folder.Body).Model).Boxes["wholesale"];

        // Act.
        var refusal = await session.MoveElementToAsync("wholesale", box.X + 200, box.Y + 5, TestContext.Current.CancellationToken);

        // Assert.
        Assert.Equal("", refusal);
        Assert.Equal(original, await File.ReadAllBytesAsync(_folder.Body, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(-1000, "a", false)]
    [InlineData(1000, "b", true)]
    public void APlace_IsBeforeTheFirstLowerNode_OrAfterTheLast(double centreY, string anchor, bool after)
    {
        // Arrange: a small column of three.
        var layout = SankeyLayout.Of(SankeyParser.Parse(LineDocument.Parse(
            "sankey: 1\nnodes:\n  - id: a\n  - id: m\n  - id: b\n  - id: z\n    column: 2\nflows:\n" +
            "  - from: a\n    to: z\n    value: 1\n  - from: m\n    to: z\n    value: 1\n  - from: b\n    to: z\n    value: 1\n")));

        // Act.
        var place = SankeySession.PlaceOf(layout, "m", centreY);

        // Assert.
        Assert.Equal((anchor, after), place);
    }

    [Fact]
    public async Task AReadOnlySession_RefusesAMove()
    {
        // Arrange.
        await using var session = new SankeySession(_folder.Body, _store, new SankeyElementMapper());

        // Act.
        var refusal = await session.MoveElementToAsync("cafes", 0, 10_000, TestContext.Current.CancellationToken);

        // Assert.
        Assert.Contains("read-only", refusal, StringComparison.Ordinal);
    }

    private SankeyModel Parse() => SankeyParser.Parse(LineDocument.Parse(File.ReadAllText(_folder.Body)));

    private static IReadOnlyList<DiagramElement> Delivered(IReadOnlyList<DiagramDelta> baseline) =>
        Assert.IsType<DiagramAddDelta>(Assert.Single(baseline)).Elements;

    private static SankeyNodePayload Node(IReadOnlyList<DiagramElement> delivered, string id) =>
        SankeyNodePayload.Parser.ParseFrom(delivered.Single(element => element.Id == id).Payload.ToArray());

    private static SankeyFlowPayload Flow(IReadOnlyList<DiagramElement> delivered, string id) =>
        SankeyFlowPayload.Parser.ParseFrom(delivered.Single(element => element.Id == id).Payload.ToArray());
}
