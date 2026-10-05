using EtAlii.Adp.Context;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Documents.Wire;
using EtAlii.Adp.History;
using Xunit;

namespace EtAlii.Adp.Diagram.Sankey.Tests;

/// <summary>The menus, the property grid and the palette, each editing through the commands.</summary>
public sealed class SankeyProvidersTests : IDisposable
{
    private readonly SankeyTestFolder _folder = new(System.IO.Path.Combine(AppContext.BaseDirectory, "Fixtures", "lf-line-endings.skv"), "small.skv");
    private readonly SankeyDocumentStore _store = new();
    private readonly HistoryStackStore _historyStacks;
    private readonly SankeyContextPropertyProvider _properties;
    private readonly SankeyContextActionProvider _actions;

    public SankeyProvidersTests()
    {
        _historyStacks = new HistoryStackStore(new SankeyTestDispatcher(_store));
        _properties = new SankeyContextPropertyProvider(_historyStacks, _store);
        _actions = new SankeyContextActionProvider(_historyStacks, _store);
    }

    public void Dispose()
    {
        _historyStacks.Dispose();
        _folder.Dispose();
    }

    [Fact]
    public async Task ANode_HasItsRows_WithItsValueShownButNotEditable()
    {
        // Act.
        var rows = await _properties.DescribeAsync(Target("m"), TestContext.Current.CancellationToken);

        // Assert.
        Assert.Equal(
            [
                SankeyContextPropertyProvider.NameProperty, SankeyContextPropertyProvider.DescriptionProperty,
                SankeyContextPropertyProvider.ColorProperty, SankeyContextPropertyProvider.CustomColorProperty,
                SankeyContextPropertyProvider.ColumnProperty, SankeyContextPropertyProvider.ValueProperty,
                SankeyContextPropertyProvider.NoteProperty, SankeyContextPropertyProvider.FormatProperty,
            ],
            rows.Select(row => row.Id));
        var value = rows.Single(row => row.Id == SankeyContextPropertyProvider.ValueProperty);
        Assert.Equal("10", value.Value);
        Assert.False(value.IsEditable);
        var column = rows.Single(row => row.Id == SankeyContextPropertyProvider.ColumnProperty);
        Assert.Equal(SankeyContextPropertyProvider.AutoColumn, column.Value);
        Assert.Equal([SankeyContextPropertyProvider.AutoColumn, "1", "2", "3", "4"], column.Choices);
        Assert.Equal(SankeyContextPropertyProvider.DefaultColor, rows.Single(row => row.Id == SankeyContextPropertyProvider.ColorProperty).Value);
    }

    [Fact]
    public async Task ACustomColour_ShowsAsCustom_WithItsHexInItsOwnRow()
    {
        // Act.
        var rows = await _properties.DescribeAsync(Target("x"), TestContext.Current.CancellationToken);

        // Assert.
        var color = rows.Single(row => row.Id == SankeyContextPropertyProvider.ColorProperty);
        Assert.Equal(SankeyContextPropertyProvider.CustomColor, color.Value);
        Assert.Contains(SankeyContextPropertyProvider.CustomColor, color.Choices);
        Assert.Equal("#336699", rows.Single(row => row.Id == SankeyContextPropertyProvider.CustomColorProperty).Value);
    }

    [Fact]
    public async Task AFlow_HasItsValueAndStep_EditableInTheGrid()
    {
        // Act.
        var rows = await _properties.DescribeAsync(Target("m->x"), TestContext.Current.CancellationToken);
        var set = await _properties.SetAsync(Target("m->x"), SankeyContextPropertyProvider.ValueProperty, "8.5", TestContext.Current.CancellationToken);

        // Assert.
        Assert.Equal("7", rows.Single(row => row.Id == SankeyContextPropertyProvider.ValueProperty).Value);
        Assert.Equal("0.5", rows.Single(row => row.Id == SankeyContextPropertyProvider.StepProperty).Value);
        Assert.True(set.IsSuccess, set.Error);
        Assert.Equal(8.5, Parse().Flows.Single(flow => flow.Id == "m->x").Value);
    }

    [Fact]
    public async Task ThePaletteChoice_WritesTheWord_DefaultClearsIt_AndCustomTakesAHex()
    {
        // Act.
        var word = await _properties.SetAsync(Target("b"), SankeyContextPropertyProvider.ColorProperty, "teal", TestContext.Current.CancellationToken);
        var cleared = await _properties.SetAsync(Target("a"), SankeyContextPropertyProvider.ColorProperty, SankeyContextPropertyProvider.DefaultColor, TestContext.Current.CancellationToken);
        var hex = await _properties.SetAsync(Target("y"), SankeyContextPropertyProvider.CustomColorProperty, "#aa3300", TestContext.Current.CancellationToken);
        var bad = await _properties.SetAsync(Target("y"), SankeyContextPropertyProvider.CustomColorProperty, "#zz", TestContext.Current.CancellationToken);

        // Assert.
        Assert.True(word.IsSuccess && cleared.IsSuccess && hex.IsSuccess);
        Assert.False(bad.IsSuccess);
        var nodes = Parse().Nodes.ToDictionary(node => node.Id);
        Assert.Equal(("teal", "", "#aa3300"), (nodes["b"].Color, nodes["a"].Color, nodes["y"].Color));
    }

    [Fact]
    public async Task AutoColumn_RemovesTheStatedColumn()
    {
        // Act.
        await _properties.SetAsync(Target("y"), SankeyContextPropertyProvider.ColumnProperty, "4", TestContext.Current.CancellationToken);
        var stated = Parse().Nodes.Single(node => node.Id == "y").Column;
        await _properties.SetAsync(Target("y"), SankeyContextPropertyProvider.ColumnProperty, SankeyContextPropertyProvider.AutoColumn, TestContext.Current.CancellationToken);

        // Assert.
        Assert.Equal(4, stated);
        Assert.Null(Parse().Nodes.Single(node => node.Id == "y").Column);
    }

    [Fact]
    public async Task IncreaseAndDecrease_AreOnAFlowsMenu_WithTheirShortcuts_AndStepIt()
    {
        // Act.
        var groups = await _actions.DiscoverAsync(Target("m->x"), TestContext.Current.CancellationToken);
        var increased = await _actions.ExecuteAsync(Target("m->x"), SankeyContextActionProvider.IncreaseActionId, TestContext.Current.CancellationToken);

        // Assert.
        var actions = groups.SelectMany(group => group.Actions).ToList();
        Assert.Equal("+", actions.Single(action => action.Id == SankeyContextActionProvider.IncreaseActionId).Shortcut?.Key);
        Assert.Equal("-", actions.Single(action => action.Id == SankeyContextActionProvider.DecreaseActionId).Shortcut?.Key);
        Assert.Contains(actions, action => action.Id == SankeyContextActionProvider.ThickerActionId);
        Assert.IsType<ContextExecutionCompleted>(increased);
        Assert.Equal(7.5, Parse().Flows.Single(flow => flow.Id == "m->x").Value);
    }

    [Fact]
    public async Task RenamingANode_AsksForTheName_ThenWritesIt()
    {
        // Act.
        var asked = await _actions.ExecuteAsync(Target("a"), SankeyContextActionProvider.RenameActionId, TestContext.Current.CancellationToken);
        var empty = await _actions.ValidateAsync(Target("a"), SankeyContextActionProvider.RenameActionId, " ", TestContext.Current.CancellationToken);
        var committed = await _actions.CommitAsync(Target("a"), SankeyContextActionProvider.RenameActionId, "Origin", "", TestContext.Current.CancellationToken);

        // Assert.
        Assert.Equal("Source A", Assert.IsType<ContextExecutionRequiresInput>(asked).Request.InitialValue);
        Assert.False(empty.Valid);
        Assert.True(committed.Completed, committed.Error);
        Assert.Equal("Origin", Parse().Nodes[0].Name);
    }

    [Fact]
    public async Task RemovingANodeWithFlows_AsksFirst_ThenRemovesItsFlowsToo()
    {
        // Act.
        var asked = await _actions.ExecuteAsync(Target("m"), SankeyContextActionProvider.RemoveActionId, TestContext.Current.CancellationToken);
        var committed = await _actions.CommitAsync(Target("m"), SankeyContextActionProvider.RemoveActionId, "", "", TestContext.Current.CancellationToken);

        // Assert.
        Assert.Contains("4 flows", Assert.IsType<ContextExecutionRequiresConfirmation>(asked).Request.Message, StringComparison.Ordinal);
        Assert.True(committed.Completed, committed.Error);
        Assert.Empty(Parse().Flows);
    }

    [Fact]
    public async Task ADropAndAGesture_DiscoverTheAddAndTheConnect_AndExecuteThem()
    {
        // Act.
        var offered = (await _actions.DiscoverAsync(Target(GestureIds.Placement(700, 900)), TestContext.Current.CancellationToken)).SelectMany(group => group.Actions).Select(action => action.Id).ToList();
        var added = await _actions.ExecuteAsync(Target(GestureIds.Placement(700, 900)), SankeyContextActionProvider.AddActionId, TestContext.Current.CancellationToken);
        var connected = await _actions.ExecuteAsync(Target(GestureIds.Relation("a", "x")), SankeyContextActionProvider.ConnectActionId, TestContext.Current.CancellationToken);

        // Assert.
        Assert.Equal([SankeyContextActionProvider.AddActionId, SankeyContextActionProvider.ArrangeActionId, SankeyContextActionProvider.ThickerActionId, SankeyContextActionProvider.ThinnerActionId], offered);
        Assert.IsType<ContextExecutionCompleted>(added);
        Assert.IsType<ContextExecutionCompleted>(connected);
        var model = Parse();
        Assert.Equal(3, model.Nodes.Single(node => node.Name == "New node").Column);
        Assert.Contains(model.Flows, flow => flow.From == "a" && flow.To == "x");
    }

    [Fact]
    public async Task ThickerAndThinner_RescaleTheWholeDiagram()
    {
        // Act.
        var thicker = await _actions.ExecuteAsync(Target(GestureIds.Placement(0, 0)), SankeyContextActionProvider.ThickerActionId, TestContext.Current.CancellationToken);

        // Assert.
        Assert.IsType<ContextExecutionCompleted>(thicker);
        Assert.Equal(1.25, Parse().Settings.Thickness);
    }

    [Fact]
    public void ThePalette_OffersANode_AddedByTheAddAction()
    {
        // Act.
        var item = Assert.Single(new SankeyToolboxProvider().Items);

        // Assert.
        Assert.Equal(SankeyContextActionProvider.AddActionId, item.DropActionId);
    }

    private ContextTarget Target(string elementId) =>
        new(ContextScope.DiagramElement, _folder.Body, IsContainer: false, SourceId: default, _folder.Folder, ShortGuid.NewShortGuid(), elementId);

    private SankeyModel Parse() => SankeyParser.Parse(LineDocument.Parse(File.ReadAllText(_folder.Body)));
}
