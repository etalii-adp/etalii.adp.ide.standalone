using EtAlii.Adp.Documents;
using Xunit;

namespace EtAlii.Adp.Diagram.SupplyChain.Tests;

/// <summary>
/// A session over the automotive example: what it delivers, and that a selection reaches the canvas
/// as the deltas of exactly the elements whose marking moved.
/// </summary>
public sealed class SupplyChainSessionTests : IDisposable
{
    private readonly SupplyChainTestFolder _folder = new(SupplyChainExamples.Automotive, "automotive.supply");
    private readonly SupplyChainDocumentStore _store = new();
    private readonly SupplyChainSelections _selections = new();
    private readonly ShortGuid _watchId = ShortGuid.NewShortGuid();

    public void Dispose() => _folder.Dispose();

    [Fact]
    public async Task AFreshSession_DeliversEveryGroupNodeAndFlow_GroupsFirst()
    {
        // Arrange.
        var model = SupplyChainParser.Parse(LineDocument.Parse(await File.ReadAllTextAsync(_folder.Body, TestContext.Current.CancellationToken)));
        await using var session = Open();

        // Act.
        var delivered = Delivered(session.Baseline());

        // Assert.
        var expected = model.Groups.Select(group => group.Id).Concat(model.Nodes.Select(node => node.Id)).Concat(model.Flows.Select(flow => flow.Id));
        Assert.Equal(expected.Order(StringComparer.Ordinal), delivered.Select(element => element.Id).Order(StringComparer.Ordinal));
        Assert.All(delivered.Take(model.Groups.Count), element => Assert.Equal(SupplyChainElementMapper.GroupType, element.Type));
        Assert.All(delivered.Where(element => element.Type != SupplyChainElementMapper.FlowType), element => Assert.Equal("", TraceOf(element)));
    }

    [Fact]
    public async Task ANodeArrives_WithItsStageTypeAndQuantity_AndAFlowWithItsWeight()
    {
        // Arrange.
        await using var session = Open();

        // Act.
        var delivered = Delivered(session.Baseline());

        // Assert.
        var plant = delivered.Single(element => element.Id == "vehicle-plant");
        Assert.Equal(SupplyChainElementMapper.AssemblerType, plant.Type);
        var payload = SupplyChainNodePayload.Parser.ParseFrom(plant.Payload.ToArray());
        Assert.Equal(("Vehicle assembly plant", 2.8, "M vehicles", "europe"), (payload.Name, payload.Quantity, payload.Unit, payload.GroupId));

        // The heaviest flow in its own unit is drawn at full width, a lighter one narrower.
        var heaviest = Flow(delivered, "vehicle-plant-to-port-logistics");
        var lighter = Flow(delivered, "port-logistics-to-fleet-leasing");
        Assert.Equal(1, heaviest.Weight);
        Assert.Equal(0.7 / 2.6, lighter.Weight, 6);
    }

    [Fact]
    public async Task ASelection_ArrivesAsTheTrace_AndClearingItTakesTheTraceAway()
    {
        // Arrange.
        await using var session = Open();
        session.Baseline();
        var raised = Watch(session);

        // Act.
        var selection = _selections.Select(_watchId, _folder.Body, "pack-assembly");
        var traced = await Next(raised);

        // Assert.
        var marked = Changed(traced);
        Assert.Equal(SupplyChainTrace.Selected, TraceOf(marked["pack-assembly"]));
        Assert.Equal(SupplyChainTrace.Upstream, TraceOf(marked["kolwezi-cobalt"]));
        Assert.Equal(SupplyChainTrace.Dimmed, TraceOf(marked["thai-rubber"]));

        // Act: the selection moves on.
        raised = Watch(session);
        selection.Dispose();
        var cleared = Changed(await Next(raised));

        // Assert.
        Assert.Equal("", TraceOf(cleared["pack-assembly"]));
        Assert.Equal("", TraceOf(cleared["thai-rubber"]));
    }

    [Fact]
    public async Task ASelectionOnAnotherConnection_LightsNothingHere_WhileOneOnThisConnectionDoes()
    {
        // Arrange.
        await using var session = Open();
        session.Baseline();
        var raised = Watch(session);

        // Act.
        using (_selections.Select(ShortGuid.NewShortGuid(), _folder.Body, "pack-assembly"))
        {
            await Task.Delay(200, TestContext.Current.CancellationToken);
        }

        // Assert: nothing - and then, beside that absence, a selection here that does arrive.
        Assert.False(raised.Task.IsCompleted);
        using var mine = _selections.Select(_watchId, _folder.Body, "pack-assembly");
        Assert.NotEmpty(Changed(await Next(raised)));
    }

    [Fact]
    public async Task DraggingAGroup_MovesEveryMemberByTheSameDistance()
    {
        // Arrange.
        using var history = new History.HistoryStack(new SupplyChainTestDispatcher(_store));
        await using var session = new SupplyChainSession(_watchId, _folder.Body, _store, new SupplyChainElementMapper(), _selections, history);
        var before = SupplyChainLayout.Of(_store.GetOrLoad(_folder.Body).Model);
        var frame = before.GroupBoxes["central-africa"];

        // Act.
        var refusal = await session.MoveElementToAsync("central-africa", frame.X + 40, frame.Y - 30, TestContext.Current.CancellationToken);

        // Assert.
        Assert.Equal("", refusal);
        var after = SupplyChainParser.Parse(LineDocument.Parse(await File.ReadAllTextAsync(_folder.Body, TestContext.Current.CancellationToken)));
        var cobalt = after.Nodes.Single(node => node.Id == "kolwezi-cobalt");
        Assert.Equal(before.NodeBoxes["kolwezi-cobalt"].X + 40, cobalt.X);
        Assert.Equal(before.NodeBoxes["kolwezi-cobalt"].Y - 30, cobalt.Y);
    }

    private SupplyChainSession Open() => new(_watchId, _folder.Body, _store, new SupplyChainElementMapper(), _selections);

    private static TaskCompletionSource<DiagramDeltasEventArgs> Watch(SupplyChainSession session)
    {
        var raised = new TaskCompletionSource<DiagramDeltasEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        session.Changed += (_, args) => raised.TrySetResult(args);
        return raised;
    }

    private static Task<DiagramDeltasEventArgs> Next(TaskCompletionSource<DiagramDeltasEventArgs> raised) =>
        raised.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

    private static Dictionary<string, DiagramElement> Changed(DiagramDeltasEventArgs args) =>
        args.Deltas.OfType<DiagramAddDelta>().SelectMany(delta => delta.Elements).ToDictionary(element => element.Id, StringComparer.Ordinal);

    private static IReadOnlyList<DiagramElement> Delivered(IReadOnlyList<DiagramDelta> baseline) =>
        Assert.IsType<DiagramAddDelta>(Assert.Single(baseline)).Elements;

    private static SupplyChainFlowPayload Flow(IReadOnlyList<DiagramElement> delivered, string id) =>
        SupplyChainFlowPayload.Parser.ParseFrom(delivered.Single(element => element.Id == id).Payload.ToArray());

    private static string TraceOf(DiagramElement element) => element.Type switch
    {
        SupplyChainElementMapper.GroupType => SupplyChainGroupPayload.Parser.ParseFrom(element.Payload.ToArray()).Trace,
        SupplyChainElementMapper.FlowType => SupplyChainFlowPayload.Parser.ParseFrom(element.Payload.ToArray()).Trace,
        _ => SupplyChainNodePayload.Parser.ParseFrom(element.Payload.ToArray()).Trace,
    };
}
