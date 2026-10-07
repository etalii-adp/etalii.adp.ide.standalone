using EtAlii.Adp.Documents;
using Xunit;

namespace EtAlii.Adp.Diagram.Rdf.Tests;

/// <summary>
/// The scheme projection (skos-diagram Requirement 1): everything by assertion, one hierarchy
/// edge per pair from either direction with no completion anywhere, mappings drawn only in-file,
/// collections never conflated with the hierarchy, and the budget cutting the top of the
/// vocabulary rather than a triple-order slice (Requirement 8).
/// </summary>
public class SkosProjectionTests
{
    private const string Prelude = """
        @prefix skos: <http://www.w3.org/2004/02/skos/core#> .
        @prefix ex: <http://example.org/> .

        """;

    private static SkosProjectionResult Project(string turtle, int budget = RdfProjection.DefaultBudget) =>
        SkosProjection.Project(RdfParser.Parse(LineDocument.Parse(Prelude + turtle)), budget);

    [Fact]
    public void EitherAssertedDirection_IsOneEdge_AndNothingIsCompleted()
    {
        // Arrange: broader one way, narrower the other way, and one pair asserted both ways.
        var projection = Project("""
            ex:scheme a skos:ConceptScheme .
            ex:a a skos:Concept ; skos:inScheme ex:scheme .
            ex:b a skos:Concept ; skos:inScheme ex:scheme ; skos:broader ex:a .
            ex:c a skos:Concept ; skos:inScheme ex:scheme .
            ex:d a skos:Concept ; skos:inScheme ex:scheme ; skos:broader ex:c .
            ex:c skos:narrower ex:d .
            ex:e a skos:Concept ; skos:inScheme ex:scheme .
            ex:a skos:narrower ex:e .
            """);

        // Assert: three hierarchy edges - b under a (broader-asserted), d under c (both ways,
        // one edge, two triples), e under a (narrower-asserted) - each on the canonical id.
        var hierarchy = projection.Edges.Where(edge => edge.Kind == SkosEdgeKind.Hierarchy).ToList();
        Assert.Equal(3, hierarchy.Count);

        var bUnderA = Assert.Single(hierarchy, edge => edge.ToId == "res:http://example.org/b");
        Assert.Equal("res:http://example.org/a", bUnderA.FromId);
        Assert.Equal("edge:res:http://example.org/b|http://www.w3.org/2004/02/skos/core#broader|res:http://example.org/a", bUnderA.Id);
        Assert.False(bUnderA.AssertedBothWays);
        Assert.Single(bUnderA.Triples);

        var dUnderC = Assert.Single(hierarchy, edge => edge.ToId == "res:http://example.org/d");
        Assert.True(dUnderC.AssertedBothWays);
        Assert.Equal(2, dUnderC.Triples.Count); // Both stating triples carried - disconnect and validation need both.

        var eUnderA = Assert.Single(hierarchy, edge => edge.ToId == "res:http://example.org/e");
        Assert.Equal("res:http://example.org/a", eUnderA.FromId);
        Assert.Single(eUnderA.Triples); // Projection of the asserted triple; no synthesized inverse.
    }

    [Fact]
    public void Membership_IsAssertionOnly_AndTheUnfiledBandCatchesTheRest()
    {
        // Arrange: c hangs under a member concept but asserts no membership itself - the letter
        // of Requirement 1.1 files it in the unfiled band, hierarchy notwithstanding.
        var projection = Project("""
            ex:scheme a skos:ConceptScheme ; skos:hasTopConcept ex:a .
            ex:a a skos:Concept ; skos:topConceptOf ex:scheme .
            ex:b a skos:Concept ; skos:inScheme ex:scheme ; skos:broader ex:a .
            ex:c a skos:Concept ; skos:broader ex:b .
            """);

        // Assert.
        var a = Assert.Single(projection.Concepts, concept => concept.Iri == "http://example.org/a");
        Assert.Equal(["http://example.org/scheme"], a.SchemeIris);
        var c = Assert.Single(projection.Concepts, concept => concept.Iri == "http://example.org/c");
        Assert.Empty(c.SchemeIris);
        var scheme = Assert.Single(projection.Schemes);
        Assert.Equal(["res:http://example.org/a"], scheme.TopConceptIds);
    }

    [Fact]
    public void Mappings_DrawOnlyInFile_AndSurfaceAsRowsOtherwise()
    {
        // Arrange.
        var projection = Project("""
            ex:a a skos:Concept ; skos:exactMatch ex:b ; skos:closeMatch <http://elsewhere.org/x> .
            ex:b a skos:Concept .
            """);

        // Assert: one dotted edge in-file; the out-of-file mapping is a row, never a stub node.
        var mapping = Assert.Single(projection.Edges, edge => edge.Kind == SkosEdgeKind.Mapping);
        Assert.Equal("res:http://example.org/a", mapping.FromId);
        Assert.Equal("res:http://example.org/b", mapping.ToId);
        var row = Assert.Single(projection.OutOfFileMappings);
        Assert.Equal("http://www.w3.org/2004/02/skos/core#closeMatch", row.Predicate.Iri);
        Assert.DoesNotContain(projection.Concepts, concept => concept.Iri == "http://elsewhere.org/x");
    }

    [Fact]
    public void Collections_KeepTheirOrder_AndStayOffTheHierarchy()
    {
        // Arrange: an ordered collection via memberList, and labels and notations on the side.
        var projection = Project("""
            ex:a a skos:Concept ; skos:prefLabel "Milk"@en ; skos:notation "M1" .
            ex:b a skos:Concept .
            ex:group a skos:OrderedCollection ; skos:prefLabel "Additives"@en ; skos:memberList ( ex:b ex:a ) .
            """);

        // Assert.
        var group = Assert.Single(projection.Collections);
        Assert.True(group.Ordered);
        Assert.Equal(["res:http://example.org/b", "res:http://example.org/a"], group.MemberIds);
        Assert.DoesNotContain(projection.Edges, edge => edge.Kind == SkosEdgeKind.Hierarchy);
        var a = Assert.Single(projection.Concepts, concept => concept.Iri == "http://example.org/a");
        Assert.Equal(["M1"], a.Notations);
        Assert.Contains(a.Labels, label => label is { Source: SkosLabelSource.Preferred, Text: "Milk", Language: "en" });
    }

    [Fact]
    public void TheBudget_CutsTheTopOfTheVocabulary_NotATripleOrderSlice()
    {
        // Arrange: the scheme, its top and a child - then an unfiled straggler declared FIRST in
        // the file, which document order would keep and hierarchy-aware order cuts.
        var projection = Project("""
            ex:zzz a skos:Concept .
            ex:scheme a skos:ConceptScheme ; skos:hasTopConcept ex:top .
            ex:top a skos:Concept ; skos:topConceptOf ex:scheme .
            ex:child a skos:Concept ; skos:inScheme ex:scheme ; skos:broader ex:top .
            """, budget: 3);

        // Assert: scheme, top, child survive; the unfiled straggler is the cut; totals honest.
        Assert.True(projection.Truncated);
        Assert.Equal(3, projection.Shown);
        Assert.Equal(4, projection.Total);
        Assert.Single(projection.Schemes);
        Assert.Equal(
            ["http://example.org/top", "http://example.org/child"],
            projection.Concepts.Select(concept => concept.Iri));
    }

    [Fact]
    public void PolyhierarchyDrawsOnce_AndSkosXlIsDetectedButNeverRead()
    {
        // Arrange.
        var model = RdfParser.Parse(LineDocument.Parse(Prelude + """
            @prefix skosxl: <http://www.w3.org/2008/05/skos-xl#> .
            ex:a a skos:Concept .
            ex:b a skos:Concept .
            ex:c a skos:Concept ; skos:broader ex:a ; skos:broader ex:b ; skosxl:prefLabel ex:cLabel .
            """));
        var projection = SkosProjection.Project(model);

        // Assert: one node, two hierarchy edges - and the XL triple is a fact for validation,
        // not a label for the chooser.
        Assert.Single(projection.Concepts, concept => concept.Iri == "http://example.org/c");
        Assert.Equal(2, projection.Edges.Count(edge => edge is { Kind: SkosEdgeKind.Hierarchy, ToId: "res:http://example.org/c" }));
        Assert.True(SkosProjection.HasXlLabels(model));
        var c = Assert.Single(projection.Concepts, concept => concept.Iri == "http://example.org/c");
        Assert.Empty(c.Labels);
    }
}
