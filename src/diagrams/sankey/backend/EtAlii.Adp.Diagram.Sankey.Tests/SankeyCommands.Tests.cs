using EtAlii.Adp.Documents;
using EtAlii.Adp.History;
using Xunit;

namespace EtAlii.Adp.Diagram.Sankey.Tests;

/// <summary>
/// Every command edits, every undo gives back the original bytes, and every refusal writes nothing -
/// through the real store on a real file.
/// </summary>
public sealed class SankeyCommandsTests : IDisposable
{
    private readonly SankeyTestFolder _folder = new(Path.Combine(AppContext.BaseDirectory, "Fixtures", "crlf-line-endings.skv"), "small.skv");
    private readonly SankeyDocumentStore _store = new();
    private readonly SankeyTestDispatcher _dispatcher;
    private readonly byte[] _original;

    public SankeyCommandsTests()
    {
        _original = File.ReadAllBytes(Body);
        _dispatcher = new SankeyTestDispatcher(_store);
    }

    private string Body => _folder.Body;

    public void Dispose() => _folder.Dispose();

    public static TheoryData<string> EveryEdit =>
    [
        "add a node", "add a node in a column", "connect", "remove a node", "remove a flow", "move a node up", "move a node to the end",
        "rename", "colour a node", "custom colour", "clear a colour", "note", "format", "column", "describe a flow",
        "set a value", "set a step", "increase", "decrease", "thicker", "thinner",
    ];

    private ICommand EditNamed(string name) => name switch
    {
        "add a node" => new AddSankeyNodeCommand(Body, 0, 900),
        "add a node in a column" => new AddSankeyNodeCommand(Body, SankeyGeometry.ColumnPitch * 3, 0),
        "connect" => new ConnectSankeyNodesCommand(Body, "a", "x"),
        "remove a node" => new RemoveSankeyEntryCommand(Body, "m"),
        "remove a flow" => new RemoveSankeyEntryCommand(Body, "m->y"),
        "move a node up" => new MoveSankeyNodeCommand(Body, "y", "x", After: false),
        "move a node to the end" => new MoveSankeyNodeCommand(Body, "a", "y", After: true),
        "rename" => new SetSankeyPropertyCommand(Body, "a", SankeyKeys.Name, "Source: A"),
        "colour a node" => new SetSankeyPropertyCommand(Body, "b", SankeyKeys.Color, "teal"),
        "custom colour" => new SetSankeyPropertyCommand(Body, "a", SankeyKeys.Color, "#c0ffee"),
        "clear a colour" => new SetSankeyPropertyCommand(Body, "x", SankeyKeys.Color, ""),
        "note" => new SetSankeyPropertyCommand(Body, "x", SankeyKeys.Note, "+3% Y/Y"),
        "format" => new SetSankeyPropertyCommand(Body, "y", SankeyKeys.Format, "({value} t)"),
        "column" => new SetSankeyPropertyCommand(Body, "y", SankeyKeys.Column, "4"),
        "describe a flow" => new SetSankeyPropertyCommand(Body, "a->m", SankeyKeys.Description, "Most of it."),
        "set a value" => new SetSankeyPropertyCommand(Body, "a->m", SankeyKeys.Value, "7.25"),
        "set a step" => new SetSankeyPropertyCommand(Body, "a->m", SankeyKeys.Step, "0.25"),
        "increase" => new StepSankeyValueCommand(Body, "m->x", 1),
        "decrease" => new StepSankeyValueCommand(Body, "a->m", -1),
        "thicker" => new ScaleSankeyThicknessCommand(Body, 1),
        "thinner" => new ScaleSankeyThicknessCommand(Body, -1),
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
        { "step a node", "change a flow's value" },
        { "a negative value", "zero or more" },
        { "a value that is not a number", "zero or more" },
        { "a step of zero", "step of zero" },
        { "an unknown colour", "not a colour" },
        { "a column of zero", "not a column" },
        { "an empty name", "needs a name" },
        { "remove what is not there", "no longer" },
        { "a value on a node", "cannot be set" },
        { "move next to itself", "next to itself" },
        { "thinner than thinnest", "already as thin" },
    };

    private ICommand RefusalNamed(string name) => name switch
    {
        "connect a node to itself" => new ConnectSankeyNodesCommand(Body, "a", "a"),
        "connect twice" => new ConnectSankeyNodesCommand(Body, "a", "m"),
        "connect to nothing" => new ConnectSankeyNodesCommand(Body, "a", "nowhere"),
        "decrease below zero" => new StepSankeyValueCommand(Body, "m->y", -1),
        "step a node" => new StepSankeyValueCommand(Body, "m", 1),
        "a negative value" => new SetSankeyPropertyCommand(Body, "a->m", SankeyKeys.Value, "-3"),
        "a value that is not a number" => new SetSankeyPropertyCommand(Body, "a->m", SankeyKeys.Value, "lots"),
        "a step of zero" => new SetSankeyPropertyCommand(Body, "a->m", SankeyKeys.Step, "0"),
        "an unknown colour" => new SetSankeyPropertyCommand(Body, "a", SankeyKeys.Color, "chartreuse"),
        "a column of zero" => new SetSankeyPropertyCommand(Body, "a", SankeyKeys.Column, "0"),
        "an empty name" => new SetSankeyPropertyCommand(Body, "a", SankeyKeys.Name, " "),
        "remove what is not there" => new RemoveSankeyEntryCommand(Body, "nothing-here"),
        "a value on a node" => new SetSankeyPropertyCommand(Body, "a", SankeyKeys.Value, "3"),
        "move next to itself" => new MoveSankeyNodeCommand(Body, "a", "a", After: true),
        "thinner than thinnest" => new ScaleSankeyThicknessCommand(Body, -100),
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, "No such refusal."),
    };

    [Theory]
    [MemberData(nameof(EveryRefusal))]
    public async Task EveryRefusal_WritesNothing_AndSaysWhy(string refusal, string saying)
    {
        // Arrange: two refusals need the document at an end of its range first; that state is
        // then the original. The cache is loaded, so an in-place edit would have somewhere to hide.
        if (refusal == "decrease below zero")
        {
            await Dispatch(new SetSankeyPropertyCommand(Body, "m->y", SankeyKeys.Value, "0"));
        }
        else if (refusal == "thinner than thinnest")
        {
            await Dispatch(new ScaleSankeyThicknessCommand(Body, -100));
        }

        var bytesBefore = await File.ReadAllBytesAsync(Body, TestContext.Current.CancellationToken);
        var cachedBefore = _store.GetOrLoad(Body).Document.Text;
        var command = RefusalNamed(refusal);

        // Act.
        var result = await _dispatcher.DispatchAsync(command, TestContext.Current.CancellationToken);

        // Assert.
        Assert.False(result.IsSuccess);
        Assert.Contains(saying, result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(bytesBefore, await File.ReadAllBytesAsync(Body, TestContext.Current.CancellationToken));
        Assert.Equal(cachedBefore, _store.GetOrLoad(Body).Document.Text);
    }

    [Fact]
    public async Task Increasing_AddsTheFlowsOwnStep_AndOtherwiseATenthOfItsMagnitude()
    {
        // Act: m->x steps by its own 0.5, and a->m, which states none, by a tenth of its magnitude: 0.1.
        await Dispatch(new StepSankeyValueCommand(Body, "m->x", 2));
        await Dispatch(new StepSankeyValueCommand(Body, "a->m", -3));

        // Assert.
        var model = Parse();
        Assert.Equal(8, model.Flows.Single(flow => flow.Id == "m->x").Value);
        Assert.Equal(5.7, model.Flows.Single(flow => flow.Id == "a->m").Value);
    }

    [Theory]
    [InlineData(null, 0.1)]
    [InlineData(0.5, 0.1)]
    [InlineData(6d, 0.1)]
    [InlineData(42d, 1d)]
    [InlineData(1302d, 100d)]
    public void TheDefaultStep_IsATenthOfTheValuesOrderOfMagnitude(double? value, double step)
    {
        // Act + Assert.
        Assert.Equal(step, StepSankeyValueCommandHandler.DefaultStep(value), 6);
    }

    [Fact]
    public async Task Decreasing_StopsAtZero_RatherThanGoingNegative()
    {
        // Act: 3 in steps of 0.1 is thirty steps; a hundred are asked for.
        await Dispatch(new StepSankeyValueCommand(Body, "m->y", -100));

        // Assert.
        Assert.Equal(0, Parse().Flows.Single(flow => flow.Id == "m->y").Value);
    }

    [Fact]
    public void TenStepsOfAFraction_ComeBackToAWholeNumber()
    {
        // Act.
        double? value = 0;
        for (var i = 0; i < 10; i++)
        {
            value = StepSankeyValueCommandHandler.Stepped(value, 0.1, 1);
        }

        // Assert.
        Assert.Equal(1, value);
    }

    [Fact]
    public async Task AnAddedNode_LandsInTheColumnOfTheDrop_AboveTheFirstNodeBelowIt()
    {
        // Arrange: between x and y in the third column.
        var layout = SankeyLayout.Of(Parse());
        var between = (layout.Boxes["x"].Bottom + layout.Boxes["y"].Y) / 2;

        // Act.
        await Dispatch(new AddSankeyNodeCommand(Body, SankeyLayout.LeftOf(2) + 10, between, "n"));

        // Assert.
        var model = Parse();
        Assert.Equal(["a", "b", "m", "x", "n", "y"], model.Nodes.Select(node => node.Id));
        var added = model.Nodes.Single(node => node.Id == "n");
        Assert.Equal(("New node", 3), (added.Name, added.Column!.Value));
        Assert.Equal(["x", "n", "y"], SankeyLayout.Of(model).ColumnOrder[2]);
    }

    [Fact]
    public async Task ANodeDroppedInTheFirstColumn_StatesNoColumn_AndAFurtherOneGetsAFreshName()
    {
        // Act.
        await Dispatch(new AddSankeyNodeCommand(Body, 0, 2000));
        await Dispatch(new AddSankeyNodeCommand(Body, 0, 2000));

        // Assert.
        var added = Parse().Nodes.Skip(5).ToList();
        Assert.Equal(["New node", "New node 2"], added.Select(node => node.Name));
        Assert.All(added, node => Assert.Null(node.Column));
    }

    [Fact]
    public async Task ANewFlow_CarriesOnWhatTheSourceHasNotPassedOn()
    {
        // Arrange: the middle takes in 10 and passes on 10, so first take 3 away from what it passes on.
        await Dispatch(new SetSankeyPropertyCommand(Body, "m->y", SankeyKeys.Value, "0"));

        // Act.
        await Dispatch(new ConnectSankeyNodesCommand(Body, "m", "b"));
        await Dispatch(new ConnectSankeyNodesCommand(Body, "x", "y"));

        // Assert.
        var flows = Parse().Flows;
        Assert.Equal(3, flows.Single(flow => flow.Id == "m->b").Value);
        Assert.Equal(7, flows.Single(flow => flow.Id == "x->y").Value);
    }

    [Fact]
    public async Task RemovingANode_RemovesEveryFlowToOrFromIt()
    {
        // Act.
        await Dispatch(new RemoveSankeyEntryCommand(Body, "m"));

        // Assert.
        var model = Parse();
        Assert.DoesNotContain(model.Nodes, node => node.Id == "m");
        Assert.Empty(model.Flows);
    }

    [Fact]
    public async Task Thicker_WritesTheThicknessUnderTheHeader_AndThinnerChangesThatLine()
    {
        // Act.
        await Dispatch(new ScaleSankeyThicknessCommand(Body, 1));
        await Dispatch(new ScaleSankeyThicknessCommand(Body, -2));

        // Assert.
        var text = await File.ReadAllTextAsync(Body, TestContext.Current.CancellationToken);
        Assert.StartsWith("sankey: 1\r\nthickness: 0.8\r\n", text, StringComparison.Ordinal);
        Assert.Equal(0.8, Parse().Settings.Thickness);
    }

    private async Task Dispatch(ICommand command)
    {
        var result = await _dispatcher.DispatchAsync(command, TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess, result.Error);
    }

    private SankeyModel Parse() => SankeyParser.Parse(LineDocument.Parse(File.ReadAllText(Body)));
}
