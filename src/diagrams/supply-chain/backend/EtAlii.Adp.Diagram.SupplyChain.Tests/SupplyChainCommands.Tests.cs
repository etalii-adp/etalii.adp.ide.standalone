using EtAlii.Adp.Documents;
using EtAlii.Adp.History;
using Xunit;

namespace EtAlii.Adp.Diagram.SupplyChain.Tests;

/// <summary>
/// Every command edits, every undo gives back the original bytes, and every refusal writes nothing -
/// through the real store on a real file.
/// </summary>
public sealed class SupplyChainCommandsTests : IDisposable
{
    private readonly SupplyChainTestFolder _folder = new(Path.Combine(AppContext.BaseDirectory, "Fixtures", "crlf-line-endings.supply"), "small.supply");
    private readonly SupplyChainDocumentStore _store = new();
    private readonly SupplyChainTestDispatcher _dispatcher;
    private readonly byte[] _original;

    public SupplyChainCommandsTests()
    {
        _original = File.ReadAllBytes(Body);
        _dispatcher = new SupplyChainTestDispatcher(_store);
    }

    private string Body => _folder.Body;

    public void Dispose() => _folder.Dispose();

    public static TheoryData<string> EveryEdit =>
    [
        "add a node", "connect", "remove a node", "remove a flow", "remove a group", "place", "move a group", "arrange",
        "rename", "set a quantity", "clear a quantity", "set a stage", "ungroup", "describe a flow", "increase", "decrease",
        "increase a flow", "put in a new group",
    ];

    private ICommand EditNamed(string name) => name switch
    {
        "add a node" => new AddSupplyChainNodeCommand(Body, SupplyChainNodeTypes.Distributor, 500, 500),
        "connect" => new ConnectSupplyChainNodesCommand(Body, "mine", "shop"),
        "remove a node" => new RemoveSupplyChainEntryCommand(Body, "plant"),
        "remove a flow" => new RemoveSupplyChainEntryCommand(Body, "ore"),
        "remove a group" => new RemoveSupplyChainEntryCommand(Body, "north"),
        "place" => new PlaceSupplyChainNodesCommand(Body, new Dictionary<string, (double X, double Y)> { ["mine"] = (10, 20) }),
        "move a group" => new PlaceSupplyChainNodesCommand(Body, new Dictionary<string, (double X, double Y)> { ["mine"] = (10, 20), ["plant"] = (10, 160) }),
        "arrange" => new ArrangeSupplyChainCommand(Body),
        "rename" => new SetSupplyChainPropertyCommand(Body, "mine", SupplyChainKeys.Name, "Open-pit mine"),
        "set a quantity" => new SetSupplyChainPropertyCommand(Body, "plant", SupplyChainKeys.Quantity, "7.25"),
        "clear a quantity" => new SetSupplyChainPropertyCommand(Body, "plant", SupplyChainKeys.Quantity, ""),
        "set a stage" => new SetSupplyChainPropertyCommand(Body, "plant", SupplyChainKeys.Type, SupplyChainNodeTypes.Assembler),
        "ungroup" => new SetSupplyChainPropertyCommand(Body, "plant", SupplyChainKeys.Group, ""),
        "describe a flow" => new SetSupplyChainPropertyCommand(Body, "goods", SupplyChainKeys.Description, "Boxed."),
        "increase" => new StepSupplyChainValueCommand(Body, "mine", 1),
        "decrease" => new StepSupplyChainValueCommand(Body, "plant", -1),
        "increase a flow" => new StepSupplyChainValueCommand(Body, "goods", 1),
        "put in a new group" => new GroupSupplyChainNodeCommand(Body, "shop", "High street"),
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, "No such edit."),
    };

    [Theory]
    [MemberData(nameof(EveryEdit))]
    public async Task EveryEdit_ThenItsUndo_GivesTheOriginalBytes(string edit)
    {
        // Act.
        var result = await _dispatcher.DispatchAsync(EditNamed(edit), TestContext.Current.CancellationToken);

        // Assert: it edited - otherwise the undo proves nothing - and left a document that reads cleanly.
        Assert.True(result.IsSuccess, result.Error);
        Assert.NotEqual(_original, await File.ReadAllBytesAsync(Body, TestContext.Current.CancellationToken));
        Assert.Empty(Parse().Problems);

        // Act: the undo.
        var undone = await _dispatcher.DispatchAsync(Assert.IsAssignableFrom<ICommand>(result.Inverse), TestContext.Current.CancellationToken);

        // Assert.
        Assert.True(undone.IsSuccess, undone.Error);
        Assert.Equal(_original, await File.ReadAllBytesAsync(Body, TestContext.Current.CancellationToken));
    }

    public static TheoryData<string, string> EveryRefusal => new()
    {
        { "connect a node to itself", "itself" },
        { "connect twice", "already" },
        { "connect to nothing", "one node to another" },
        { "decrease below zero", "already zero" },
        { "a negative quantity", "zero or more" },
        { "a quantity that is not a number", "zero or more" },
        { "a step of zero", "step of zero" },
        { "an unknown stage", "not a stage" },
        { "an unknown group", "no group" },
        { "step a group", "no value of its own" },
        { "remove what is not there", "no longer" },
        { "a product on a node", "cannot be set" },
    };

    private ICommand RefusalNamed(string name) => name switch
    {
        "connect a node to itself" => new ConnectSupplyChainNodesCommand(Body, "mine", "mine"),
        "connect twice" => new ConnectSupplyChainNodesCommand(Body, "mine", "plant"),
        "connect to nothing" => new ConnectSupplyChainNodesCommand(Body, "mine", "nowhere"),
        "decrease below zero" => new StepSupplyChainValueCommand(Body, "shop", -1),
        "a negative quantity" => new SetSupplyChainPropertyCommand(Body, "mine", SupplyChainKeys.Quantity, "-3"),
        "a quantity that is not a number" => new SetSupplyChainPropertyCommand(Body, "mine", SupplyChainKeys.Quantity, "lots"),
        "a step of zero" => new SetSupplyChainPropertyCommand(Body, "mine", SupplyChainKeys.Step, "0"),
        "an unknown stage" => new SetSupplyChainPropertyCommand(Body, "mine", SupplyChainKeys.Type, "quarry"),
        "an unknown group" => new SetSupplyChainPropertyCommand(Body, "mine", SupplyChainKeys.Group, "south"),
        "step a group" => new StepSupplyChainValueCommand(Body, "north", 1),
        "remove what is not there" => new RemoveSupplyChainEntryCommand(Body, "nothing-here"),
        "a product on a node" => new SetSupplyChainPropertyCommand(Body, "mine", SupplyChainKeys.Product, "Ore"),
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, "No such refusal."),
    };

    [Theory]
    [MemberData(nameof(EveryRefusal))]
    public async Task EveryRefusal_WritesNothing_AndSaysWhy(string refusal, string saying)
    {
        // Arrange: the cache is loaded, so an in-place edit would have somewhere to hide.
        var cachedBefore = _store.GetOrLoad(Body).Document.Text;

        // Act.
        var result = await _dispatcher.DispatchAsync(RefusalNamed(refusal), TestContext.Current.CancellationToken);

        // Assert.
        Assert.False(result.IsSuccess);
        Assert.Contains(saying, result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(_original, await File.ReadAllBytesAsync(Body, TestContext.Current.CancellationToken));
        Assert.Equal(cachedBefore, _store.GetOrLoad(Body).Document.Text);
    }

    [Fact]
    public async Task Increasing_AddsTheNodesOwnStep_AndDecreasingTakesItAway()
    {
        // Act: the plant steps by 0.5, the mine by the default of 1.
        await Dispatch(new StepSupplyChainValueCommand(Body, "plant", 1));
        await Dispatch(new StepSupplyChainValueCommand(Body, "mine", -3));

        // Assert.
        var model = Parse();
        Assert.Equal(4.5, model.Nodes.Single(node => node.Id == "plant").Quantity);
        Assert.Equal(7, model.Nodes.Single(node => node.Id == "mine").Quantity);
    }

    [Fact]
    public async Task Decreasing_StopsAtZero_RatherThanGoingNegative()
    {
        // Act: 4 in steps of 0.5 is eight steps; ten are asked for.
        await Dispatch(new StepSupplyChainValueCommand(Body, "plant", -10));

        // Assert.
        Assert.Equal(0, Parse().Nodes.Single(node => node.Id == "plant").Quantity);
    }

    [Fact]
    public void TenStepsOfAFraction_ComeBackToAWholeNumber()
    {
        // Act.
        double? value = 0;
        for (var i = 0; i < 10; i++)
        {
            value = StepSupplyChainValueCommandHandler.Stepped(value, 0.1, 1, "quantity");
        }

        // Assert.
        Assert.Equal(1, value);
    }

    [Fact]
    public async Task AnAddedNode_IsCentredOnTheDrop_WithAName()
    {
        // Act.
        await Dispatch(new AddSupplyChainNodeCommand(Body, SupplyChainNodeTypes.Distributor, 500, 500));

        // Assert.
        var added = Parse().Nodes.Last();
        Assert.Equal(SupplyChainNodeTypes.Distributor, added.Type);
        Assert.Equal("New distributor", added.Name);
        Assert.Equal(500 - (SupplyChainGeometry.NodeWidth / 2), added.X);
        Assert.Equal(500 - (SupplyChainGeometry.NodeHeight / 2), added.Y);
    }

    [Fact]
    public async Task PuttingANodeInANewGroup_CreatesTheGroup_AndMovesTheNodeIntoIt()
    {
        // Act.
        await Dispatch(new GroupSupplyChainNodeCommand(Body, "shop", "High street"));

        // Assert.
        var model = Parse();
        var group = model.Groups.Single(candidate => candidate.Name == "High street");
        Assert.Equal(group.Id, model.Nodes.Single(node => node.Id == "shop").Group);
    }

    [Fact]
    public async Task Arranging_PlacesEveryNode()
    {
        // Act.
        await Dispatch(new ArrangeSupplyChainCommand(Body));

        // Assert.
        Assert.All(Parse().Nodes, node => Assert.True(node.IsPlaced, node.Id));
    }

    private async Task Dispatch(ICommand command)
    {
        var result = await _dispatcher.DispatchAsync(command, TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess, result.Error);
    }

    private SupplyChainModel Parse() => SupplyChainParser.Parse(LineDocument.Parse(File.ReadAllText(Body)));
}
