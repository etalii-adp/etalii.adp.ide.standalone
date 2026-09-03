using EtAlii.Adp.Backend.Hierarchy;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.Sparql.Tests;

/// <summary>
/// Containment as a tested invariant: every region's bounds contain its contents, nested two
/// deep, for fully computed layout AND for the authored-region-anchor case - the case most
/// likely to break quietly when someone drags a region.
/// </summary>
public class SparqlLayoutTests
{
    private static readonly IReadOnlyDictionary<string, RegistrationPosition> _noStored =
        new Dictionary<string, RegistrationPosition>();

    private static SparqlProjectionResult ProjectText(string text) =>
        SparqlProjection.Project(SparqlParser.Parse(text));

    private static SparqlProjectionResult ProjectFixture(string name) =>
        ProjectText(File.ReadAllText(IoPath.Combine(AppContext.BaseDirectory, "Fixtures", name)));

    private const string NestedQuery =
        "PREFIX ex: <http://example.org/> SELECT * WHERE { ?a ex:p ?b . OPTIONAL { ?b ex:q ?c . OPTIONAL { ?c ex:r ?d } } }";

    /// <summary>The invariant itself, asserted for every region against every element it holds.</summary>
    private static void AssertContainment(SparqlProjectionResult projection, SparqlLayoutResult layout)
    {
        foreach (var region in projection.Regions)
        {
            var frame = layout.RegionBounds[region.Id];
            var prefix = region.ScopePath + "/";

            foreach (var node in projection.Nodes.Where(node =>
                         node.ScopePath == region.ScopePath || node.ScopePath.StartsWith(prefix, StringComparison.Ordinal)))
            {
                // Only computed positions owe containment: an authored node position is the
                // author's own choice of spot.
                var position = layout.NodePositions[node.Id];
                var nodeRect = new SparqlRect(position.X, position.Y, 170, 64);
                Assert.True(
                    frame.Contains(nodeRect),
                    $"{node.Id} at ({position.X},{position.Y}) escapes {region.Id} {frame}");
            }

            foreach (var nested in projection.Regions.Where(nested =>
                         nested.ScopePath.StartsWith(prefix, StringComparison.Ordinal)))
            {
                Assert.True(
                    frame.Contains(layout.RegionBounds[nested.Id]),
                    $"{nested.Id} escapes {region.Id}");
            }
        }
    }

    [Fact]
    public void ComputedLayout_KeepsEveryRegionsContentsInsideItsBounds_NestedTwoDeep()
    {
        // Arrange.
        var projection = ProjectText(NestedQuery);

        // Act.
        var layout = SparqlLayout.Compute(projection, _noStored);

        // Assert.
        Assert.Equal(2, projection.Regions.Count);
        AssertContainment(projection, layout);
    }

    [Fact]
    public void TheGroupCorpus_HoldsContainmentAcrossEveryRegionKind()
    {
        // Arrange.
        var projection = ProjectFixture("groups.rq");

        // Act.
        var layout = SparqlLayout.Compute(projection, _noStored);

        // Assert.
        AssertContainment(projection, layout);
        // Every node and every region got a place - nothing silently undrawn.
        Assert.All(projection.Nodes, node => Assert.True(layout.NodePositions.ContainsKey(node.Id)));
        Assert.All(projection.Regions, region => Assert.True(layout.RegionBounds.ContainsKey(region.Id)));
    }

    [Fact]
    public void AnAuthoredRegionAnchor_MovesTheFrameWithItsContentsFollowing()
    {
        // Arrange.
        var projection = ProjectText(NestedQuery);
        var computed = SparqlLayout.Compute(projection, _noStored);
        var outer = projection.Regions.First(region => region.ScopePath == "where/optional.0");
        var anchor = new RegistrationPosition(900, 700);

        // Act: the user dragged the outer region far away.
        var layout = SparqlLayout.Compute(projection, new Dictionary<string, RegistrationPosition>
        {
            [outer.Id] = anchor,
        });

        // Assert.
        // The frame sits at its anchor, and containment survived the move - the drag-a-region
        // case that breaks quietly when contents are left behind.
        var frame = layout.RegionBounds[outer.Id];
        Assert.Equal(anchor.X, frame.X);
        Assert.Equal(anchor.Y, frame.Y);
        AssertContainment(projection, layout);

        // The contents moved by exactly the frame's delta.
        var before = computed.NodePositions["var:c"];
        var after = layout.NodePositions["var:c"];
        Assert.Equal(anchor.X - computed.RegionBounds[outer.Id].X, after.X - before.X);
        Assert.Equal(anchor.Y - computed.RegionBounds[outer.Id].Y, after.Y - before.Y);
    }

    [Fact]
    public void AnAuthoredNodePosition_OverridesItsComputedPlace_AndBeatsARegionAnchor()
    {
        // Arrange.
        var projection = ProjectText(NestedQuery);
        var outer = projection.Regions.First(region => region.ScopePath == "where/optional.0");
        var authored = new RegistrationPosition(1500, 40);

        // Act: the node was authored absolute AND its region was anchored - the author's spot wins.
        var layout = SparqlLayout.Compute(projection, new Dictionary<string, RegistrationPosition>
        {
            ["var:c"] = authored,
            [outer.Id] = new RegistrationPosition(900, 700),
        });

        // Assert.
        Assert.Equal(authored, layout.NodePositions["var:c"]);
    }

    [Fact]
    public void AnAnonymousNode_AlwaysTakesItsComputedPlace()
    {
        // Arrange.
        var projection = ProjectText(
            "PREFIX ex: <http://example.org/> SELECT * WHERE { [ ex:p ?a ] ex:q ?b }");
        var computed = SparqlLayout.Compute(projection, _noStored);

        // Act: a stored position against an anonymous ordinal is ignored, not honored -
        // ordinals are not stable identities (Requirement 5.4).
        var layout = SparqlLayout.Compute(projection, new Dictionary<string, RegistrationPosition>
        {
            ["anon:0"] = new RegistrationPosition(999, 999),
        });

        // Assert.
        Assert.Equal(computed.NodePositions["anon:0"], layout.NodePositions["anon:0"]);
    }

    [Fact]
    public void SameInput_SameGeometry()
    {
        // Arrange.
        var projection = ProjectFixture("groups.rq");

        // Act.
        var first = SparqlLayout.Compute(projection, _noStored);
        var second = SparqlLayout.Compute(projection, _noStored);

        // Assert.
        Assert.Equal(first.NodePositions, second.NodePositions);
        Assert.Equal(first.RegionBounds, second.RegionBounds);
    }
}
