using System.Collections.Immutable;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Hierarchy;
using Xunit;

namespace EtAlii.Adp.Diagram.AgentBehaviorModelling.Tests;

public class AbmElementMapperTests
{
    private static readonly AbmModel Model = AbmParser.Parse(LineDocument.Parse(
        "## Behavior\n- **Try in order:** A\n  - **Check:** B\n    Notes the canvas never gets.\n  - **Retry up to 2 times:** C\n    - **Do:** D\n"));

    private static readonly IReadOnlyDictionary<string, RegistrationPosition> NoneStored = ImmutableDictionary<string, RegistrationPosition>.Empty;

    [Fact]
    public void EveryNode_AndALineToEveryChild()
    {
        // Act.
        var elements = new AbmElementMapper().Visible(Model, NoneStored, DiagramViewport.Unbounded);

        // Assert.
        Assert.Equal(["1", "1.1", "1.2", "1.2.1", "child:1.1", "child:1.2", "child:1.2.1"], elements.Select(element => element.Id));
        Assert.Equal(AbmElementMapper.FallbackType, elements[0].Type);
        Assert.Equal(AbmElementMapper.RetryType, elements[2].Type);
        Assert.Equal(AbmElementMapper.ChildType, elements[^1].Type);

        var retry = AbmNodePayload.Parser.ParseFrom(elements[2].Payload.ToArray());
        Assert.Equal("Retry up to 2 times", retry.Keyword);
        Assert.Equal("C", retry.Label);
        var check = AbmNodePayload.Parser.ParseFrom(elements[1].Payload.ToArray());
        Assert.True(check.HasNotes);

        var line = AbmChildPayload.Parser.ParseFrom(elements[^1].Payload.ToArray());
        Assert.Equal(("1.2", "1.2.1", 1), (line.FromElementId, line.ToElementId, line.Index));
    }

    [Fact]
    public void AStoredPosition_MovesItsRowDown_ButNotAcross()
    {
        // Arrange.
        var stored = new Dictionary<string, RegistrationPosition>(StringComparer.Ordinal) { ["1.1"] = new(1000, 2000) };
        var mapper = new AbmElementMapper();

        // Act.
        var moved = mapper.Visible(Model, stored, DiagramViewport.Unbounded).ToDictionary(element => element.Id);
        var computed = mapper.Visible(Model, NoneStored, DiagramViewport.Unbounded).ToDictionary(element => element.Id);

        // Assert: the centre is the stored top plus half the node, for the whole row; across, the order holds.
        Assert.Equal(computed["1.1"].X, moved["1.1"].X);
        Assert.Equal(2000 + (AbmLayout.NodeHeight / 2), moved["1.1"].Y);
        Assert.Equal(2000 + (AbmLayout.NodeHeight / 2), moved["1.2"].Y);
        Assert.Equal(moved["1.2"].Y + AbmLayout.NodeHeight + AbmLayout.VerticalGap, moved["1.2.1"].Y);
    }

    [Fact]
    public void AViewOfOneNode_BringsItsLinesAndTheirOtherEnds()
    {
        // Arrange: only the root is in view.
        var root = AbmLayout.Compute(Model)["1"];
        var viewport = new DiagramViewport(root.X, root.Y, root.X + 10, root.Y + 10);

        // Act.
        var ids = new AbmElementMapper().Visible(Model, NoneStored, viewport).Select(element => element.Id).ToList();

        // Assert: the root's two lines, with both children - never a line with one end missing.
        Assert.Equal(["1", "1.1", "1.2", "child:1.1", "child:1.2"], ids);
    }
}
