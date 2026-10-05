using EtAlii.Adp.Context;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Documents.Wire;
using EtAlii.Adp.History;
using Xunit;

namespace EtAlii.Adp.Diagram.SupplyChain.Tests;

/// <summary>The menus, the property grid and the palette, each editing through the commands.</summary>
public sealed class SupplyChainProvidersTests : IDisposable
{
    private readonly SupplyChainTestFolder _folder = new(System.IO.Path.Combine(AppContext.BaseDirectory, "Fixtures", "lf-line-endings.supply"), "small.supply");
    private readonly SupplyChainDocumentStore _store = new();
    private readonly HistoryStackStore _historyStacks;
    private readonly SupplyChainContextPropertyProvider _properties;
    private readonly SupplyChainContextActionProvider _actions;

    public SupplyChainProvidersTests()
    {
        _historyStacks = new HistoryStackStore(new SupplyChainTestDispatcher(_store));
        _properties = new SupplyChainContextPropertyProvider(_historyStacks, _store);
        _actions = new SupplyChainContextActionProvider(_historyStacks, _store);
    }

    public void Dispose()
    {
        _historyStacks.Dispose();
        _folder.Dispose();
    }

    [Fact]
    public async Task ANode_HasItsIdentityAndAmountRows_WithTheGroupByName()
    {
        // Act.
        var rows = await _properties.DescribeAsync(Target("mine"), TestContext.Current.CancellationToken);

        // Assert.
        Assert.Equal(
            [
                SupplyChainContextPropertyProvider.NameProperty, SupplyChainContextPropertyProvider.StageProperty,
                SupplyChainContextPropertyProvider.GroupProperty, SupplyChainContextPropertyProvider.DescriptionProperty,
                SupplyChainContextPropertyProvider.QuantityProperty, SupplyChainContextPropertyProvider.UnitProperty,
                SupplyChainContextPropertyProvider.StepProperty,
            ],
            rows.Select(row => row.Id));
        var group = rows.Single(row => row.Id == SupplyChainContextPropertyProvider.GroupProperty);
        Assert.Equal("North", group.Value);
        Assert.Equal([SupplyChainContextPropertyProvider.NoGroup, "North"], group.Choices);
        Assert.Equal("10", rows.Single(row => row.Id == SupplyChainContextPropertyProvider.QuantityProperty).Value);
        Assert.Equal("1", rows.Single(row => row.Id == SupplyChainContextPropertyProvider.StepProperty).Value);
    }

    [Fact]
    public async Task AFlowAndAGroup_HaveTheirOwnRows()
    {
        // Act.
        var flow = await _properties.DescribeAsync(Target("ore"), TestContext.Current.CancellationToken);
        var group = await _properties.DescribeAsync(Target("north"), TestContext.Current.CancellationToken);

        // Assert.
        Assert.Contains(flow, row => row.Id == SupplyChainContextPropertyProvider.VolumeProperty && row.Value == "6");
        Assert.Contains(flow, row => row.Id == SupplyChainContextPropertyProvider.ProductProperty && row.Value == "Ore");
        Assert.Equal([SupplyChainContextPropertyProvider.NameProperty, SupplyChainContextPropertyProvider.DescriptionProperty], group.Select(row => row.Id));
    }

    [Fact]
    public async Task ChoosingAGroupByName_WritesItsId_AndNoneUngroups()
    {
        // Arrange: take the shop into North, then the mine out of it.
        var into = await _properties.SetAsync(Target("shop"), SupplyChainContextPropertyProvider.GroupProperty, "North", TestContext.Current.CancellationToken);
        var outOf = await _properties.SetAsync(Target("mine"), SupplyChainContextPropertyProvider.GroupProperty, SupplyChainContextPropertyProvider.NoGroup, TestContext.Current.CancellationToken);

        // Assert.
        Assert.True(into.IsSuccess, into.Error);
        Assert.True(outOf.IsSuccess, outOf.Error);
        var model = Parse();
        Assert.Equal("north", model.Nodes.Single(node => node.Id == "shop").Group);
        Assert.Equal("", model.Nodes.Single(node => node.Id == "mine").Group);
    }

    [Fact]
    public async Task ANegativeQuantityInTheGrid_IsRefused_AndWritesNothing()
    {
        // Arrange.
        var before = await File.ReadAllBytesAsync(_folder.Body, TestContext.Current.CancellationToken);

        // Act.
        var result = await _properties.SetAsync(Target("mine"), SupplyChainContextPropertyProvider.QuantityProperty, "-1", TestContext.Current.CancellationToken);

        // Assert.
        Assert.False(result.IsSuccess);
        Assert.Equal(before, await File.ReadAllBytesAsync(_folder.Body, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task IncreaseAndDecrease_AreOnANodesMenu_WithTheirShortcuts_AndStepIt()
    {
        // Act.
        var groups = await _actions.DiscoverAsync(Target("plant"), TestContext.Current.CancellationToken);
        var increased = await _actions.ExecuteAsync(Target("plant"), SupplyChainContextActionProvider.IncreaseActionId, TestContext.Current.CancellationToken);

        // Assert.
        var actions = groups.SelectMany(group => group.Actions).ToList();
        Assert.Equal("+", actions.Single(action => action.Id == SupplyChainContextActionProvider.IncreaseActionId).Shortcut?.Key);
        Assert.Equal("-", actions.Single(action => action.Id == SupplyChainContextActionProvider.DecreaseActionId).Shortcut?.Key);
        Assert.IsType<ContextExecutionCompleted>(increased);
        Assert.Equal(4.5, Parse().Nodes.Single(node => node.Id == "plant").Quantity);
    }

    [Fact]
    public async Task DecreaseAtZero_IsOfferedButUnavailable_SayingWhy()
    {
        // Act: the shop states no quantity.
        var actions = (await _actions.DiscoverAsync(Target("shop"), TestContext.Current.CancellationToken)).SelectMany(group => group.Actions);

        // Assert.
        var decrease = actions.Single(action => action.Id == SupplyChainContextActionProvider.DecreaseActionId);
        Assert.False(decrease.Available);
        Assert.Contains("zero", decrease.UnavailableReason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RemovingANodeWithFlows_AsksFirst_ThenRemovesItsFlowsToo()
    {
        // Act.
        var asked = await _actions.ExecuteAsync(Target("plant"), SupplyChainContextActionProvider.RemoveActionId, TestContext.Current.CancellationToken);
        var committed = await _actions.CommitAsync(Target("plant"), SupplyChainContextActionProvider.RemoveActionId, "", "", TestContext.Current.CancellationToken);

        // Assert.
        Assert.Contains("2 flows", Assert.IsType<ContextExecutionRequiresConfirmation>(asked).Request.Message, StringComparison.Ordinal);
        Assert.True(committed.Completed, committed.Error);
        Assert.Empty(Parse().Flows);
    }

    [Fact]
    public async Task ADropAndAGesture_DiscoverTheAddsAndTheConnect_AndExecuteThem()
    {
        // Act.
        var adds = (await _actions.DiscoverAsync(Target(GestureIds.Placement(300, 700)), TestContext.Current.CancellationToken)).SelectMany(group => group.Actions).Select(action => action.Id).ToList();
        var added = await _actions.ExecuteAsync(Target(GestureIds.Placement(300, 700)), SupplyChainContextActionProvider.AddActionId(SupplyChainNodeTypes.Consumer), TestContext.Current.CancellationToken);
        var connected = await _actions.ExecuteAsync(Target(GestureIds.Relation("mine", "shop")), SupplyChainContextActionProvider.ConnectActionId, TestContext.Current.CancellationToken);

        // Assert.
        Assert.Equal(
            [.. SupplyChainNodeTypes.All.Select(SupplyChainContextActionProvider.AddActionId), SupplyChainContextActionProvider.AddGroupActionId, SupplyChainContextActionProvider.ArrangeActionId],
            adds);
        Assert.IsType<ContextExecutionCompleted>(added);
        Assert.IsType<ContextExecutionCompleted>(connected);
        var model = Parse();
        Assert.Contains(model.Nodes, node => node.Type == SupplyChainNodeTypes.Consumer);
        Assert.Contains(model.Flows, flow => flow.From == "mine" && flow.To == "shop");
    }

    [Fact]
    public async Task AGroupNeedsAName()
    {
        // Act.
        var empty = await _actions.ValidateAsync(Target("shop"), SupplyChainContextActionProvider.GroupActionId, " ", TestContext.Current.CancellationToken);
        var named = await _actions.ValidateAsync(Target("shop"), SupplyChainContextActionProvider.GroupActionId, "High street", TestContext.Current.CancellationToken);

        // Assert.
        Assert.False(empty.Valid);
        Assert.True(named.Valid);
    }

    [Fact]
    public void ThePalette_OffersEveryStageAndAGroup_AddedByItsOwnAction()
    {
        // Act.
        var items = new SupplyChainToolboxProvider().Items;

        // Assert.
        Assert.Equal(
            [.. SupplyChainNodeTypes.All.Select(SupplyChainContextActionProvider.AddActionId), SupplyChainContextActionProvider.AddGroupActionId],
            items.Select(item => item.DropActionId));
    }

    [Fact]
    public async Task EveryAddOnEmptyCanvas_CarriesTheIconItsPaletteEntryHas()
    {
        // Act.
        var adds = (await _actions.DiscoverAsync(Target(GestureIds.Placement(300, 700)), TestContext.Current.CancellationToken))
            .SelectMany(group => group.Actions)
            .Where(action => action.Id != SupplyChainContextActionProvider.ArrangeActionId)
            .ToDictionary(action => action.Id, action => action.Icon);

        // Assert: one icon per entry, each its palette entry's, and no two alike.
        var palette = new SupplyChainToolboxProvider().Items;
        Assert.Equal(palette.ToDictionary(item => item.DropActionId, item => item.Icon), adds);
        Assert.Equal(palette.Count, palette.Select(item => item.Icon).Distinct(StringComparer.Ordinal).Count());
    }

    private ContextTarget Target(string elementId) =>
        new(ContextScope.DiagramElement, _folder.Body, IsContainer: false, SourceId: default, _folder.Folder, ShortGuid.NewShortGuid(), elementId);

    private SupplyChainModel Parse() => SupplyChainParser.Parse(LineDocument.Parse(File.ReadAllText(_folder.Body)));
}
