using EtAlii.Adp.Documents;
using Xunit;

namespace EtAlii.Adp.Diagram.Rdf.Tests;

/// <summary>
/// The layered layout (skos-diagram Requirement 4): deterministic top-down layering with scheme
/// regions, polyhierarchy placed once, and cycles broken at the documented edge by one detection
/// pass that also feeds the validator - everything still drawn, nothing hanging.
/// </summary>
public class SkosLayoutTests
{
    private const string Prelude = """
        @prefix skos: <http://www.w3.org/2004/02/skos/core#> .
        @prefix ex: <http://example.org/> .

        """;

    private static SkosLayoutResult Layout(string turtle)
    {
        var projection = SkosProjection.Project(RdfParser.Parse(LineDocument.Parse(Prelude + turtle)));
        return SkosLayout.Layout(projection);
    }

    [Fact]
    public void TheHierarchy_LayersTopDown_BroaderAboveNarrower()
    {
        // Arrange.
        var result = Layout("""
            ex:scheme a skos:ConceptScheme ; skos:hasTopConcept ex:top .
            ex:top a skos:Concept ; skos:topConceptOf ex:scheme .
            ex:mid a skos:Concept ; skos:inScheme ex:scheme ; skos:broader ex:top .
            ex:leaf a skos:Concept ; skos:inScheme ex:scheme ; skos:broader ex:mid .
            """);

        // Assert: scheme header above its members, each layer strictly below its broader.
        var scheme = result.Positions["res:http://example.org/scheme"];
        var top = result.Positions["res:http://example.org/top"];
        var mid = result.Positions["res:http://example.org/mid"];
        var leaf = result.Positions["res:http://example.org/leaf"];
        Assert.True(scheme.Y < top.Y);
        Assert.True(top.Y < mid.Y);
        Assert.True(mid.Y < leaf.Y);
        Assert.Empty(result.Cycles);
    }

    [Fact]
    public void APolyhierarchicalConcept_IsPlacedOnce_BelowItsDeepestBroader()
    {
        // Arrange: shared sits under both top and mid; mid is deeper, so shared sits below mid.
        var result = Layout("""
            ex:scheme a skos:ConceptScheme ; skos:hasTopConcept ex:top .
            ex:top a skos:Concept ; skos:topConceptOf ex:scheme .
            ex:mid a skos:Concept ; skos:inScheme ex:scheme ; skos:broader ex:top .
            ex:shared a skos:Concept ; skos:inScheme ex:scheme ; skos:broader ex:top ; skos:broader ex:mid .
            """);

        // Assert: one position (one element), strictly below the deeper of its two broaders.
        var shared = result.Positions["res:http://example.org/shared"];
        var mid = result.Positions["res:http://example.org/mid"];
        Assert.True(shared.Y > mid.Y);
    }

    [Fact]
    public void ACycle_IsBrokenAtTheDocumentedEdge_DrawnWhole_AndReportedOnce()
    {
        // Arrange: a ⟶ b ⟶ c ⟶ a in broader terms.
        var turtle = """
            ex:scheme a skos:ConceptScheme .
            ex:a a skos:Concept ; skos:inScheme ex:scheme ; skos:broader ex:b .
            ex:b a skos:Concept ; skos:inScheme ex:scheme ; skos:broader ex:c .
            ex:c a skos:Concept ; skos:inScheme ex:scheme ; skos:broader ex:a .
            """;
        var projection = SkosProjection.Project(RdfParser.Parse(LineDocument.Parse(Prelude + turtle)));
        var result = SkosLayout.Layout(projection);

        // Assert: one cycle naming all three, the excluded edge the lowest-sorting
        // (broader, narrower) pair - (a, c), canonical id naming the broader-direction triple -
        // and every element still placed and every edge still projected.
        var cycle = Assert.Single(result.Cycles);
        Assert.Equal(
            ["res:http://example.org/a", "res:http://example.org/b", "res:http://example.org/c"],
            cycle.ConceptIds);
        Assert.Equal("edge:res:http://example.org/c|http://www.w3.org/2004/02/skos/core#broader|res:http://example.org/a", cycle.ExcludedEdgeId);
        Assert.Equal(3, cycle.Triples.Count);
        Assert.Equal(3, projection.Edges.Count(edge => edge.Kind == SkosEdgeKind.Hierarchy));
        Assert.Contains("res:http://example.org/a", result.Positions.Keys);
        Assert.Contains("res:http://example.org/b", result.Positions.Keys);
        Assert.Contains("res:http://example.org/c", result.Positions.Keys);
    }

    [Fact]
    public void TheSameFile_AlwaysLaysOutTheSameWay()
    {
        // Arrange.
        const string turtle = """
            ex:scheme a skos:ConceptScheme ; skos:hasTopConcept ex:top .
            ex:top a skos:Concept ; skos:topConceptOf ex:scheme .
            ex:b a skos:Concept ; skos:inScheme ex:scheme ; skos:broader ex:top .
            ex:a a skos:Concept ; skos:inScheme ex:scheme ; skos:broader ex:top .
            ex:unfiled a skos:Concept ; skos:related ex:a .
            ex:group a skos:Collection ; skos:member ex:a .
            """;

        // Act: two full runs from the text.
        var first = Layout(turtle);
        var second = Layout(turtle);

        // Assert: byte-equal positions, and the unfiled band strictly below the scheme's.
        Assert.Equal(
            first.Positions.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => (pair.Key, pair.Value.X, pair.Value.Y)),
            second.Positions.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => (pair.Key, pair.Value.X, pair.Value.Y)));
        Assert.True(first.Positions["res:http://example.org/unfiled"].Y > first.Positions["res:http://example.org/a"].Y);
        Assert.True(first.Positions["res:http://example.org/group"].Y > first.Positions["res:http://example.org/unfiled"].Y);
    }

    [Fact]
    public void AWideLayer_WrapsOntoCentredRows_WithSiblingsTogether()
    {
        // Arrange: two top concepts under one scheme, each with twenty narrower concepts whose ids
        // interleave (a01 under b, a02 under a, ...), so id order would scatter the siblings. The
        // forty narrower concepts are far wider than the band's column budget.
        var turtle = new System.Text.StringBuilder("""
            ex:scheme a skos:ConceptScheme ; skos:hasTopConcept ex:a , ex:b .
            ex:a a skos:Concept ; skos:topConceptOf ex:scheme .
            ex:b a skos:Concept ; skos:topConceptOf ex:scheme .

            """);
        for (var index = 1; index <= 40; index++)
        {
            turtle.Append($"ex:c{index:00} a skos:Concept ; skos:inScheme ex:scheme ; skos:broader ex:{(index % 2 == 0 ? "a" : "b")} .\n");
        }

        // Act.
        var result = Layout(turtle.ToString());

        // Assert: the layer wraps, so the band is no longer one row of forty.
        var children = Enumerable.Range(1, 40).Select(index => $"res:http://example.org/c{index:00}").ToList();
        var rows = children.Select(id => result.Positions[id].Y).Distinct().Count();
        Assert.True(rows > 1, $"The forty narrower concepts sit on {rows} row(s).");
        var width = children.Max(id => result.Positions[id].X) - children.Min(id => result.Positions[id].X);
        Assert.True(width < 40 * 240 / 2d, $"The narrower layer is {width} wide.");

        // Assert: in reading order, every narrower concept of a comes before every one of b.
        var reading = children
            .OrderBy(id => result.Positions[id].Y)
            .ThenBy(id => result.Positions[id].X)
            .Select(id => int.Parse(id[^2..], System.Globalization.CultureInfo.InvariantCulture) % 2 == 0 ? "a" : "b")
            .ToList();
        Assert.Equal(Enumerable.Repeat("a", 20).Concat(Enumerable.Repeat("b", 20)), reading);

        // Assert: the two top concepts' row is centred over the wider rows below it.
        var tops = new[] { "res:http://example.org/a", "res:http://example.org/b" }.Select(id => result.Positions[id].X).ToList();
        var left = children.Min(id => result.Positions[id].X);
        var right = children.Max(id => result.Positions[id].X);
        Assert.Equal((left + right) / 2, (tops.Min() + tops.Max()) / 2);
    }
}
