using EtAlii.Adp.Backend.Diagrams;
using Xunit;

namespace EtAlii.Adp.Diagram.CausalLoop.Tests;

/// <summary>
/// Mapping to the wire, and the filtering rule that follows from two of the three element kinds
/// having no position of their own (causal-loop-diagram Requirements 7.3, 7.4).
/// </summary>
public class CausalLoopElementMapperTests
{
    private readonly CausalLoopElementMapper _mapper = new();

    private static CausalLoopModel Model(params string[] statements) =>
        CausalLoopParser.Parse(CausalLoopDocument.Parse(string.Join("", statements.Select(s => s + "\r\n")))).Model;

    /// <summary>Three variables on a row, 100 apart, so a viewport can take any subset of them.</summary>
    private static IReadOnlyDictionary<string, CausalLoopBox> Row(params string[] ids) =>
        ids.Select((id, index) => (id, index))
            .ToDictionary(entry => entry.id, entry => new CausalLoopBox(entry.index * 100, 0, 50, 30), StringComparer.Ordinal);

    private static Wire.CausalLoopLinkPayload LinkPayload(DiagramElement element) =>
        Wire.CausalLoopLinkPayload.Parser.ParseFrom(element.Payload.Span);

    private static Wire.CausalLoopLoopPayload LoopPayload(DiagramElement element) =>
        Wire.CausalLoopLoopPayload.Parser.ParseFrom(element.Payload.Span);

    [Fact]
    public void EveryKind_BecomesAnElement()
    {
        // Arrange.
        var model = Model(
            "variable a", "variable b",
            "link a -> b +", "link b -> a +",
            "loop R1 \"pair\" a b");

        // Act.
        var elements = _mapper.Elements(model, Row("a", "b"));

        // Assert.
        Assert.NotEmpty(elements);
        Assert.Equal(2, elements.Count(element => element.Type == CausalLoopElementMapper.VariableType));
        Assert.Equal(2, elements.Count(element => element.Type == CausalLoopElementMapper.LinkType));
        Assert.Single(elements, element => element.Type == CausalLoopElementMapper.LoopType);
    }

    /// <summary>
    /// Requirement 7.3 originally decided a link by whether both endpoints survived the
    /// viewport, and the field corrected it: zooming in hid every line whose far end left the
    /// view. A link has no position, but it has a SPAN - the hull of its two end boxes - and
    /// it is drawn when that span touches the viewport, its ends riding along as anchors.
    /// </summary>
    [Fact]
    public void ALinkTravelsWhereItsSpanTouchesTheView()
    {
        // Arrange.
        var model = Model("variable a", "variable b", "variable c", "link a -> b +", "link b -> c +");

        // Act.
        // A window over a only: a -> b's span reaches into it, so the link is drawn and b comes
        // along as its anchor; b -> c's span lies wholly to the right and stays out.
        var elements = _mapper.Visible(model, Row("a", "b", "c"), new DiagramViewport(-10, -10, 60, 100));

        // Assert.
        var links = elements.Where(element => element.Type == CausalLoopElementMapper.LinkType).ToArray();
        Assert.Equal("link:a|b", Assert.Single(links).Id);
        Assert.Equal(
            ["variable:a", "variable:b"],
            elements.Where(element => element.Type == CausalLoopElementMapper.VariableType).Select(element => element.Id));
    }

    [Fact]
    public void ALinkBetweenTwoOffscreenVariables_StillCrossesTheView()
    {
        // Arrange: the zoomed-in reading of a long line - both ends outside the window, the
        // line running straight through it.
        var model = Model("variable a", "variable b", "link a -> b +");
        var far = new Dictionary<string, CausalLoopBox>(StringComparer.Ordinal)
        {
            ["a"] = new(0, 0, 50, 30),
            ["b"] = new(1000, 0, 50, 30),
        };

        // Act.
        var elements = _mapper.Visible(model, far, new DiagramViewport(400, -10, 600, 100));

        // Assert: the link is drawn, and both ends are delivered so it has something to be
        // drawn between.
        Assert.Contains(elements, element => element.Type == CausalLoopElementMapper.LinkType);
        Assert.Equal(2, elements.Count(element => element.Type == CausalLoopElementMapper.VariableType));
    }

    [Fact]
    public void ALoopLabelTravelsOnlyWhereAllItsMembersDo()
    {
        // Arrange: four members far enough apart that c is neither in view nor the anchor of
        // any view-crossing link - so the loop's members are a genuine subset, and a label
        // placed among a subset would sit somewhere the loop is not.
        var model = Model(
            "variable a", "variable b", "variable c", "variable d",
            "link a -> b +", "link b -> c +", "link c -> d +", "link d -> a +",
            "loop R1 \"four\" a b c d");
        var spread = new Dictionary<string, CausalLoopBox>(StringComparer.Ordinal)
        {
            ["a"] = new(0, 0, 50, 30),
            ["b"] = new(300, 0, 50, 30),
            ["c"] = new(600, 0, 50, 30),
            ["d"] = new(900, 0, 50, 30),
        };

        // Act.
        var narrow = _mapper.Visible(model, spread, new DiagramViewport(-10, -10, 60, 100));
        var whole = _mapper.Elements(model, spread);

        // Assert.
        Assert.DoesNotContain(narrow, element => element.Id == "variable:c");
        Assert.DoesNotContain(narrow, element => element.Type == CausalLoopElementMapper.LoopType);
        Assert.Contains(whole, element => element.Type == CausalLoopElementMapper.LoopType);
    }

    /// <summary>
    /// Requirement 7.2's choice, made visible: a variable the layout did not place is left out
    /// deliberately rather than being drawn at the origin.
    /// </summary>
    [Fact]
    public void AnUnplacedVariable_IsNotDrawn_AndIsNotDrawnAtTheOrigin()
    {
        // Arrange.
        var model = Model("variable a", "variable unplaced", "link a -> unplaced +");

        // Act.
        var elements = _mapper.Elements(model, Row("a"));

        // Assert.
        var variables = elements.Where(element => element.Type == CausalLoopElementMapper.VariableType).ToArray();
        Assert.Single(variables);
        Assert.Equal("variable:a", variables[0].Id);
        // And nothing was drawn at (0, 0) to stand in for it.
        Assert.DoesNotContain(elements, element => element.Id == "variable:unplaced");
        // The link had nowhere to land either, so it is not drawn.
        Assert.DoesNotContain(elements, element => element.Type == CausalLoopElementMapper.LinkType);
    }

    [Fact]
    public void AVariableCarriesTheSizeTheLayoutReserved()
    {
        // Arrange & act.
        var model = Model("variable a \"Population\"");
        var boxes = CausalLoopLayout.Compute(model);
        var element = Assert.Single(_mapper.Elements(model, boxes));

        // Assert.
        // The canvas draws at the size the backend computed rather than measuring the text a
        // second time and disagreeing with it.
        var payload = Wire.CausalLoopVariablePayload.Parser.ParseFrom(element.Payload.Span);
        Assert.Equal(boxes["a"].Width, payload.Width);
        Assert.Equal(boxes["a"].Height, payload.Height);
        Assert.Equal("Population", payload.Display);
    }

    [Fact]
    public void ALinkCarriesItsPolarityDelayAndWeight()
    {
        // Arrange & act.
        var model = Model("variable a", "variable b", "link a -> b - delayed weight=2.5 \"note\"");
        var link = Assert.Single(_mapper.Elements(model, Row("a", "b")), e => e.Type == CausalLoopElementMapper.LinkType);

        // Assert.
        var payload = LinkPayload(link);
        Assert.Equal(Wire.CausalLoopPolarityProto.Negative, payload.Polarity);
        Assert.True(payload.Delayed);
        Assert.Equal(2.5, payload.Weight);
        Assert.True(payload.HasWeight);
        Assert.Equal("note", payload.Label);
    }

    /// <summary>
    /// proto3 cannot tell an absent double from zero, and a weight of zero is something an
    /// author may deliberately write - so the flag carries the distinction rather than the value.
    /// </summary>
    [Fact]
    public void AnAbsentWeight_IsTellableFromAWeightOfZero()
    {
        // Arrange & act.
        var absent = LinkPayload(Assert.Single(
            _mapper.Elements(Model("variable a", "variable b", "link a -> b +"), Row("a", "b")),
            element => element.Type == CausalLoopElementMapper.LinkType));

        var zero = LinkPayload(Assert.Single(
            _mapper.Elements(Model("variable a", "variable b", "link a -> b + weight=0"), Row("a", "b")),
            element => element.Type == CausalLoopElementMapper.LinkType));

        // Assert.
        Assert.Equal(0, absent.Weight);
        Assert.Equal(0, zero.Weight);
        Assert.False(absent.HasWeight);
        Assert.True(zero.HasWeight);
    }

    [Fact]
    public void AnUnstatedPolarity_CrossesTheWireAsUnstated()
    {
        // Arrange & act.
        var link = Assert.Single(
            _mapper.Elements(Model("variable a", "variable b", "link a -> b"), Row("a", "b")),
            element => element.Type == CausalLoopElementMapper.LinkType);

        // Assert.
        Assert.Equal(Wire.CausalLoopPolarityProto.Unstated, LinkPayload(link).Polarity);
    }

    [Fact]
    public void ALoopCarriesBothItsClaimAndTheArithmetic()
    {
        // Arrange.
        // One negative link is an odd count, so the arrows make this balancing - but it says R1.
        var model = Model(
            "variable a", "variable b",
            "link a -> b +", "link b -> a -",
            "loop R1 \"claims to reinforce\" a b");

        // Act.
        var loop = Assert.Single(_mapper.Elements(model, Row("a", "b")), e => e.Type == CausalLoopElementMapper.LoopType);

        // Assert.
        var payload = LoopPayload(loop);
        Assert.Equal("R1", payload.Identifier);
        Assert.Equal(Wire.LoopPolarityProto.Balancing, payload.Computed);
        Assert.True(payload.Disagrees);
    }

    [Fact]
    public void AnUndecidableLoop_IsNotADisagreement()
    {
        // Arrange & act.
        var model = Model("variable a", "variable b", "link a -> b +", "link b -> a", "loop R1 \"unmarked\" a b");
        var loop = Assert.Single(_mapper.Elements(model, Row("a", "b")), e => e.Type == CausalLoopElementMapper.LoopType);

        // Assert.
        // Nothing is claimed to be wrong with the label when the arithmetic could not be done.
        var payload = LoopPayload(loop);
        Assert.Equal(Wire.LoopPolarityProto.Undecidable, payload.Computed);
        Assert.False(payload.Disagrees);
    }

    [Fact]
    public void ALoopLabelSitsAmongItsMembers()
    {
        // Arrange & act.
        var model = Model("variable a", "variable b", "link a -> b +", "link b -> a +", "loop R1 \"pair\" a b");
        var boxes = Row("a", "b");
        var loop = Assert.Single(_mapper.Elements(model, boxes), e => e.Type == CausalLoopElementMapper.LoopType);

        // Assert.
        Assert.Equal((boxes["a"].CenterX + boxes["b"].CenterX) / 2, loop.X);
        Assert.Equal((boxes["a"].CenterY + boxes["b"].CenterY) / 2, loop.Y);
    }

    [Fact]
    public void TheUnfilteredMapping_IsTheUnboundedViewport()
    {
        // Arrange.
        var model = Model("variable a", "variable b", "link a -> b +", "link b -> a +", "loop R1 \"pair\" a b");
        var boxes = Row("a", "b");

        // Act & assert.
        // Stated rather than left to whoever reads the two call sites: Elements is Visible with
        // an infinite viewport, so the two cannot drift apart.
        Assert.Equal(
            _mapper.Elements(model, boxes).Select(element => element.Id),
            _mapper.Visible(model, boxes, DiagramViewport.Unbounded).Select(element => element.Id));
    }
}
