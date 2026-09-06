using EtAlii.Adp.Backend.Diagrams;
using EtAlii.Adp.Backend.Hierarchy;
using EtAlii.Adp.Common;
using Xunit;

namespace EtAlii.Adp.Diagram.DependencyGraph.Tests;

/// <summary>
/// The boundary between the module's vocabulary and core's: elements out, a placement back.
/// </summary>
public class DependencyGraphElementMapperTests
{
    private readonly DependencyGraphElementMapper _mapper = new();

    private static DependencyGraphModel Parse(string yaml) =>
        DependencyGraphParser.Parse(LineDocument.Parse(yaml));

    private const string TwoNodesAndARelation = """
        dependencies: 1
        elements:
          - id: aaa
            label: API gateway
            x: 240
            row: 0
          - id: bbb
            label: Identity service
            x: 480.5
            row: 2
        relations:
          - id: ccc
            from: aaa
            to: bbb
            label: verifies tokens with
        """;

    [Fact]
    public void ANodesPositionIsItsAuthoredXAndItsRow()
    {
        // Act.
        var elements = _mapper.Elements(Parse(TwoNodesAndARelation));

        // Assert.
        // x passes through untouched - there is no projection left to get wrong, which is the
        // whole of what the timeline's scale did and the whole of what this fork deleted. y still
        // goes through the row converter, because rows were never time.
        var first = elements.Single(element => element.Id == "aaa");
        Assert.Equal(240d, first.X);
        Assert.Equal(DependencyGraphRows.ToY(0), first.Y);

        var second = elements.Single(element => element.Id == "bbb");
        Assert.Equal(480.5d, second.X);
        Assert.Equal(DependencyGraphRows.ToY(2), second.Y);
    }

    private const string TwoCoastsAndAnIsland = """
        dependencies: 1
        elements:
          - id: near
            label: Near coast
            x: 0
            row: 0
          - id: far
            label: Far coast
            x: 10000
            row: 0
          - id: island
            label: Unrelated island
            x: 20000
            row: 0
        relations:
          - id: crossing
            from: near
            to: far
            label: ships to
        """;

    /// <summary>
    /// Found in the field: zooming in hid every relation whose far end left the view. A
    /// relation is drawn where its SPAN - the hull of its two end boxes - touches the
    /// viewport, and its ends ride along as anchors; what shares no crossing span stays out.
    /// </summary>
    [Fact]
    public void ARelationTravelsWhereItsSpanTouchesTheView()
    {
        // Act: a window over the near coast only.
        var overNear = _mapper.Visible(Parse(TwoCoastsAndAnIsland), new DiagramViewport(-10, -10, 60, 100));

        // Assert: the relation is drawn and brings the far coast with it; the island is not.
        Assert.Contains(overNear, element => element.Id == "crossing");
        Assert.Contains(overNear, element => element.Id == "far");
        Assert.DoesNotContain(overNear, element => element.Id == "island");
    }

    [Fact]
    public void ARelationBetweenTwoOffscreenNodes_StillCrossesTheView()
    {
        // Act: the zoomed-in reading of a long line - both ends outside, the line through it.
        var betweenCoasts = _mapper.Visible(Parse(TwoCoastsAndAnIsland), new DiagramViewport(4000, -10, 6000, 100));

        // Assert.
        Assert.Contains(betweenCoasts, element => element.Id == "crossing");
        Assert.Contains(betweenCoasts, element => element.Id == "near");
        Assert.Contains(betweenCoasts, element => element.Id == "far");
        Assert.DoesNotContain(betweenCoasts, element => element.Id == "island");
    }

    [Fact]
    public void NodesAndRelationsCarryDifferentTypes()
    {
        // Act.
        var elements = _mapper.Elements(Parse(TwoNodesAndARelation));

        // Assert.
        // The canvas renders by type without reading the payload. One node type, not the
        // timeline's two: a period and a moment were both times, and there is only one kind of
        // thing here.
        Assert.Equal(DependencyGraphElementMapper.NodeType, elements.Single(element => element.Id == "aaa").Type);
        Assert.Equal(DependencyGraphElementMapper.NodeType, elements.Single(element => element.Id == "bbb").Type);
        Assert.Equal(DependencyGraphElementMapper.RelationType, elements.Single(element => element.Id == "ccc").Type);
    }

    [Fact]
    public void ThePayloadCarriesTheCoordinateRowAndLabel()
    {
        // Act.
        var elements = _mapper.Elements(Parse(TwoNodesAndARelation));
        var payload = DependencyGraphElementPayload.Parser.ParseFrom(
            elements.Single(element => element.Id == "aaa").Payload.Span);

        // Assert.
        Assert.Equal(240d, payload.X);
        Assert.Equal(0, payload.Row);

        // The label travels in the payload because the core Element has none of its own. Its
        // absence in the module this was forked from surfaced as a rename that produced no wire
        // change at all.
        Assert.Equal("API gateway", payload.Label);
    }

    [Fact]
    public void ARelationCarriesItsEndsInOrder_SoTheArrowheadKnowsWhichEndIsWhich()
    {
        // Act.
        var elements = _mapper.Elements(Parse(TwoNodesAndARelation));
        var payload = DependencyGraphRelationPayload.Parser.ParseFrom(
            elements.Single(element => element.Id == "ccc").Payload.Span);

        // Assert.
        // `from` depends on `to`, and the canvas draws the arrowhead at `to`. Swapping these
        // would reverse every dependency in the diagram while drawing something that still looks
        // like a graph - the silent failure this type has that the timeline did not.
        Assert.Equal("aaa", payload.FromElementId);
        Assert.Equal("bbb", payload.ToElementId);
        Assert.Equal("verifies tokens with", payload.Label);
    }

    [Fact]
    public void ARelationToNowhere_StillGoesOut()
    {
        // Arrange.
        // The validator reports it; the canvas marks it. Dropping it here would hide the problem
        // from both.
        var model = Parse("dependencies: 1\nelements:\n  - id: a\n    x: 0\nrelations:\n  - id: c\n    from: a\n    to: ghost\n");

        // Act.
        var elements = _mapper.Elements(model);

        // Assert.
        Assert.Contains(elements, element => element.Id == "c");
    }

    [Fact]
    public void ANodeWithAnUnreadableCoordinate_IsStillDelivered()
    {
        // Arrange & act.
        var model = Parse("dependencies: 1\nelements:\n  - id: broken\n    x: sideways\n    row: 1\n");
        var elements = _mapper.Elements(model);

        // Assert.
        // Visible and selectable at the origin while the panel names nothing - a coordinate that
        // will not read is zero, which is a place, so the node is simply somewhere the author did
        // not intend rather than missing.
        var element = Assert.Single(elements);
        Assert.Equal(0d, element.X);
        Assert.Equal(DependencyGraphRows.ToY(1), element.Y);
    }

    [Fact]
    public void AnUnchangedRender_ProducesNoDeltas()
    {
        // Arrange.
        // The Same guard: ReadOnlyMemory equality compares references, so without it two
        // identical renders would re-deliver every element on every re-parse.
        var before = _mapper.Elements(Parse(TwoNodesAndARelation));
        var after = _mapper.Elements(Parse(TwoNodesAndARelation));

        // Act & assert.
        Assert.Empty(_mapper.Diff(before, after));
    }

    [Fact]
    public void AChangedNode_IsOneAdd_AndARemovedOneIsOneRemove()
    {
        // Arrange.
        // Edited line by line rather than by substring: a raw string literal carries the source
        // file's own line endings, so a "\n"-pattern replace works on an LF checkout and finds
        // nothing on a CRLF one.
        var before = _mapper.Elements(Parse(TwoNodesAndARelation));
        var edited = string.Join(
            "\n",
            TwoNodesAndARelation
                .ReplaceLineEndings("\n")
                .Split('\n')
                .Where(line => !line.Contains("bbb", StringComparison.Ordinal)
                    && !line.Contains("label: Identity service", StringComparison.Ordinal)
                    && !line.Contains("x: 480.5", StringComparison.Ordinal)
                    && !line.Contains("row: 2", StringComparison.Ordinal))
                .Select(line => line.Replace("label: API gateway", "label: Renamed", StringComparison.Ordinal)));
        var after = _mapper.Elements(Parse(edited));

        // Act.
        var deltas = _mapper.Diff(before, after);

        // Assert.
        // An edit is an add carrying the new state; a removal is a remove naming the id.
        var removes = deltas.OfType<DiagramRemoveDelta>().Single();
        Assert.Contains("bbb", removes.ElementIds);
        var adds = deltas.OfType<DiagramAddDelta>().Single();
        Assert.Contains(adds.Elements, element => element.Id == "aaa");
    }

    [Fact]
    public void ADraggedNode_LandsAtTheCoordinateItWasGiven()
    {
        // Act.
        // The timeline had to carry a duration across, re-derive an end and preserve a precision
        // here. A node's x is its x.
        var (x, row) = DependencyGraphElementMapper.Placement(917.5d, DependencyGraphRows.ToY(5));

        // Assert.
        Assert.Equal(917.5d, x);
        Assert.Equal(5, row);
    }

    [Fact]
    public void ADraggedNode_SnapsOnlyItsRow()
    {
        // Act.
        // The horizontal is free and the vertical snaps: that asymmetry is the type's placement
        // model, and rounding x would quietly make the canvas a grid.
        var (x, row) = DependencyGraphElementMapper.Placement(
            -180.25d, DependencyGraphRows.ToY(2) + DependencyGraphRows.Height * 0.2);

        // Assert.
        Assert.Equal(-180.25d, x);
        Assert.Equal(2, row);
    }
}
