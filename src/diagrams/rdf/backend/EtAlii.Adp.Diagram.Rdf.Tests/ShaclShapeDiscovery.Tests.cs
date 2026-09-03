using EtAlii.Adp.Diagram.Rdf.Shacl;
using Xunit;

namespace EtAlii.Adp.Diagram.Rdf.Tests;

/// <summary>
/// The declaration-or-use discovery, one fixture per clause of the recommendation's shape
/// definition, plus the two exclusions that keep this reading honest: literals never, and
/// non-shape subjects never (shacl-diagram Requirement 1.1).
/// </summary>
public class ShaclShapeDiscoveryTests
{
    private const string Prelude = """
        @prefix sh: <http://www.w3.org/ns/shacl#> .
        @prefix ex: <http://example.org/> .
        @prefix rdfs: <http://www.w3.org/2000/01/rdf-schema#> .

        """;

    private static IReadOnlyList<ShaclShape> Discover(string body) =>
        ShaclShapeDiscovery.Discover(RdfParser.Parse(RdfDocument.Parse(Prelude + body)));

    [Fact]
    public void ATypedNodeShape_IsDiscovered()
    {
        var shapes = Discover("ex:PersonShape a sh:NodeShape .\n");
        var shape = Assert.Single(shapes);
        Assert.Equal("i:http://example.org/PersonShape", shape.Key);
        Assert.False(shape.IsPropertyShape);
    }

    [Fact]
    public void ATypedPropertyShape_IsDiscovered_AndItsPathMakesItAPropertyShape()
    {
        var shapes = Discover("ex:nameShape a sh:PropertyShape ; sh:path ex:name .\n");
        var shape = Assert.Single(shapes);
        Assert.Equal("i:http://example.org/nameShape", shape.Key);
        Assert.True(shape.IsPropertyShape);
    }

    [Fact]
    public void ASubjectOfEachTargetPredicate_IsDiscovered()
    {
        var shapes = Discover("""
            ex:A sh:targetClass ex:Person .
            ex:B sh:targetNode ex:alice .
            ex:C sh:targetSubjectsOf ex:knows .
            ex:D sh:targetObjectsOf ex:knows .
            """ + "\n");
        Assert.Equal(
            ["i:http://example.org/A", "i:http://example.org/B", "i:http://example.org/C", "i:http://example.org/D"],
            shapes.Select(shape => shape.Key));
    }

    [Fact]
    public void ASubjectOfAConstraintParameterAlone_IsDiscovered()
    {
        // No type, no target, no reference - shape purely by parameter use.
        var shapes = Discover("ex:S sh:minCount 1 .\n");
        Assert.Equal("i:http://example.org/S", Assert.Single(shapes).Key);
    }

    [Fact]
    public void ObjectsOfShapeExpectingParameters_AreDiscovered_BlankOnesIncluded()
    {
        var shapes = Discover("""
            ex:S a sh:NodeShape ;
              sh:node ex:Other ;
              sh:not [ sh:datatype ex:T ] ;
              sh:property [ sh:path ex:p ] .
            """ + "\n");

        Assert.Contains(shapes, shape => shape.Key == "i:http://example.org/Other");

        // The blank sh:not operand and the blank property shape are both shapes; the property
        // shape is one by its sh:path, the operand by its parameter too - and both by position.
        var blanks = shapes.Where(shape => shape.Key.StartsWith("b:", StringComparison.Ordinal)).ToArray();
        Assert.Equal(2, blanks.Length);
        Assert.Contains(blanks, shape => shape.IsPropertyShape);
        Assert.Contains(blanks, shape => !shape.IsPropertyShape);
    }

    [Fact]
    public void MembersOfCombinatorLists_AreDiscovered()
    {
        var shapes = Discover("ex:S sh:or ( ex:A ex:B [ sh:datatype ex:T ] ) .\n");

        Assert.Contains(shapes, shape => shape.Key == "i:http://example.org/S");
        Assert.Contains(shapes, shape => shape.Key == "i:http://example.org/A");
        Assert.Contains(shapes, shape => shape.Key == "i:http://example.org/B");
        Assert.Contains(shapes, shape => shape.Key.StartsWith("b:", StringComparison.Ordinal));

        // The list's own cons nodes are plumbing, not shapes.
        Assert.Equal(4, shapes.Count);
    }

    [Fact]
    public void AnImplicitClassTarget_IsFlagged_DirectlyAndThroughStatedSubclassing()
    {
        var shapes = Discover("""
            ex:Person a rdfs:Class, sh:NodeShape .
            ex:Special rdfs:subClassOf rdfs:Class .
            ex:Employee a ex:Special ;
              sh:property [ sh:path ex:name ] .
            ex:Plain a sh:NodeShape .
            """ + "\n");

        Assert.True(Single(shapes, "i:http://example.org/Person").ImplicitClassTarget);
        Assert.True(Single(shapes, "i:http://example.org/Employee").ImplicitClassTarget);
        Assert.False(Single(shapes, "i:http://example.org/Plain").ImplicitClassTarget);
    }

    [Fact]
    public void NonShapeSubjects_AreNeverShapes()
    {
        // Co-resident ontology and instance content: this reading leaves it to its siblings.
        var shapes = Discover("""
            ex:Person a rdfs:Class .
            ex:alice a ex:Person ; ex:name "Alice" .
            ex:S sh:targetClass ex:Person .
            """ + "\n");

        Assert.Equal("i:http://example.org/S", Assert.Single(shapes).Key);
    }

    [Fact]
    public void Ordering_IsDocumentOrderOfTheFirstDefiningTriple()
    {
        // ex:B becomes a shape on line one (as sh:node's object) before its own block; ex:A
        // follows; the blank property shape comes last.
        var shapes = Discover("""
            ex:S a sh:NodeShape ; sh:node ex:B .
            ex:A sh:targetClass ex:Person .
            ex:B sh:property [ sh:path ex:p ] .
            """ + "\n");

        Assert.Equal(
            ["i:http://example.org/S", "i:http://example.org/B", "i:http://example.org/A"],
            shapes.Where(shape => shape.Key.StartsWith("i:", StringComparison.Ordinal)).Select(shape => shape.Key));
        Assert.True(shapes[0].FirstTripleIndex < shapes[1].FirstTripleIndex);
        Assert.True(Single(shapes, "i:http://example.org/B").FirstTripleIndex
            < Single(shapes, "i:http://example.org/A").FirstTripleIndex);
    }

    [Fact]
    public void TwoSpellingsOfOneIri_AreOneShape()
    {
        var shapes = Discover("""
            ex:S a sh:NodeShape .
            <http://example.org/S> sh:targetClass ex:Person .
            """ + "\n");

        Assert.Equal("i:http://example.org/S", Assert.Single(shapes).Key);
    }

    [Fact]
    public void ACyclicCombinatorList_YieldsWhatItHasAndStops()
    {
        // Hand-broken cons plumbing: the list's rest points back at its head. Discovery must not
        // hang or throw - the validator owns judging broken files.
        var shapes = Discover("""
            ex:S sh:or _:l1 .
            _:l1 <http://www.w3.org/1999/02/22-rdf-syntax-ns#first> ex:A .
            _:l1 <http://www.w3.org/1999/02/22-rdf-syntax-ns#rest> _:l1 .
            """ + "\n");

        Assert.Contains(shapes, shape => shape.Key == "i:http://example.org/S");
        Assert.Contains(shapes, shape => shape.Key == "i:http://example.org/A");
    }

    private static ShaclShape Single(IReadOnlyList<ShaclShape> shapes, string key) =>
        shapes.Single(shape => shape.Key == key);
}
