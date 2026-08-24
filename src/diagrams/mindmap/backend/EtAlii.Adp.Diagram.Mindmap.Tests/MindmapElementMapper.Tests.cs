using EtAlii.Adp.Backend;
using EtAlii.Adp.Backend.Diagrams;
using Xunit;

namespace EtAlii.Adp.Diagram.Mindmap.Tests;

/// <summary>
/// Which nodes a viewport delivers (Requirement 11.5). Two rules decide it, and both exist so
/// the canvas never shows a half-drawn map: a node the viewport touches at all is delivered,
/// and so is whatever sits at the other end of a connector leaving one - otherwise the line
/// from an on-screen node to an off-screen parent has nothing to anchor to and vanishes.
/// </summary>
public class MindmapElementMapperTests
{
    private readonly MindmapViewState _views = new();
    private readonly MindmapElementMapper _mapper = new(MindmapMetrics.Default);
    private readonly ShortGuid _watchId = ShortGuid.NewShortGuid();

    /// <summary>root -> a -> a1, plus a sibling b, all on the right so the geometry is easy to reason about.</summary>
    private static MindmapDocument Document() => MindmapDocument.Parse(
        "<map version=\"freeplane 1.11.5\">\n" +
        "<node TEXT=\"root\" ID=\"root\">\n" +
        "<node TEXT=\"a\" ID=\"a\" POSITION=\"right\">\n" +
        "<node TEXT=\"a1\" ID=\"a1\"/>\n" +
        "</node>\n" +
        "<node TEXT=\"b\" ID=\"b\" POSITION=\"right\"/>\n" +
        "</node>\n" +
        "</map>\n");

    private MindmapViewState.ConnectionView View(MindmapDocument document) => _views.For(_watchId, "map.mm", document);

    private string[] VisibleIds(MindmapDocument document, DiagramViewport viewport) =>
        _mapper.Visible(document, View(document), viewport).Select(element => element.Id).ToArray();

    /// <summary>The viewport that is exactly one node's box - nothing else is inside it.</summary>
    private DiagramViewport JustAround(MindmapDocument document, string nodeId)
    {
        var box = _mapper.Layout(document, View(document))[nodeId];
        return new DiagramViewport(box.X, box.Y, box.Right, box.Bottom);
    }

    [Fact]
    public void Visible_DeliversANodeTheViewportOnlyPartlyCovers()
    {
        // A node straddling the edge of the view is half on screen: culling it would blank out
        // a node the user can plainly see part of.
        var document = Document();
        var box = _mapper.Layout(document, View(document))["a"];
        // A viewport whose right edge cuts "a" down the middle, and which reaches no further.
        var viewport = new DiagramViewport(box.CenterX, box.CenterY, box.Right + 1, box.Bottom + 1);

        Assert.Contains("a", VisibleIds(document, viewport));
    }

    [Fact]
    public void Visible_DeliversANodeTheViewportMerelyTouches()
    {
        // The boundary case of the rule above: sharing an edge still counts as being in view.
        var document = Document();
        var box = _mapper.Layout(document, View(document))["a"];
        var viewport = new DiagramViewport(box.Right, box.Bottom, box.Right + 100, box.Bottom + 100);

        Assert.Contains("a", VisibleIds(document, viewport));
    }

    [Fact]
    public void Visible_DeliversTheParentOfAnOnScreenNode_EvenWhenTheParentIsOutOfView()
    {
        // The connector from "a" up to "root" leaves the viewport; the canvas draws it from the
        // parent's box, so the parent has to come along or the line is simply not drawn.
        var document = Document();

        var visible = VisibleIds(document, JustAround(document, "a"));

        Assert.Contains("a", visible);
        Assert.Contains("root", visible);
    }

    [Fact]
    public void Visible_DeliversTheChildrenOfAnOnScreenNode_EvenWhenTheyAreOutOfView()
    {
        // The same rule the other way round: the line from "a" out to "a1" starts on screen.
        var document = Document();

        var visible = VisibleIds(document, JustAround(document, "a"));

        Assert.Contains("a1", visible);
    }

    [Fact]
    public void Visible_StopsAtOneHop_SoAViewportDoesNotDragInTheWholeMap()
    {
        // "b" is a sibling: reachable only through "root", which is itself only here as an
        // anchor. Following partners of partners would deliver the entire tree and defeat the
        // virtualization the viewport exists for.
        var document = Document();

        var visible = VisibleIds(document, JustAround(document, "a"));

        Assert.DoesNotContain("b", visible);
    }

    [Fact]
    public void Visible_DeliversNothingUnreachable_WhenTheViewportIsFarOffTheMap()
    {
        var document = Document();

        Assert.Empty(VisibleIds(document, new DiagramViewport(100000, 100000, 200000, 200000)));
    }

    [Fact]
    public void Visible_DeliversEveryNode_WhenTheViewportIsUnbounded()
    {
        var document = Document();

        Assert.Equal(4, VisibleIds(document, DiagramViewport.Unbounded).Length);
    }

    [Fact]
    public void Visible_NeverDeliversANodeTwice_WhenItIsBothInViewAndAPartner()
    {
        // "a" is inside the view and also the parent of "a1" and the child of "root"; the
        // union must still name it once, or the client receives a duplicate add.
        var document = Document();

        var visible = VisibleIds(document, JustAround(document, "a"));

        Assert.Equal(visible.Length, visible.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Visible_NeverDeliversANodeHiddenUnderAFold_EvenAsAPartner()
    {
        // A folded branch's children are not laid out at all; being the partner of an on-screen
        // node must not smuggle one back onto the canvas.
        var document = Document();
        var view = View(document);
        _views.Toggle(_watchId, "map.mm", document, "a"); // collapse "a", hiding "a1"

        var visible = _mapper.Visible(document, view, JustAround(document, "a")).Select(element => element.Id).ToArray();

        Assert.Contains("a", visible);
        Assert.DoesNotContain("a1", visible);
    }
}
