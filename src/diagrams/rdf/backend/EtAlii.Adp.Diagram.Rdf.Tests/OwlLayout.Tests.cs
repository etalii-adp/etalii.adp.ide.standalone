using EtAlii.Adp.Backend.Hierarchy;
using Xunit;

namespace EtAlii.Adp.Diagram.Rdf.Tests;

/// <summary>
/// The ontology layout: subclass-depth columns with cycle collapse, satellite stacking beside
/// owners, the individuals band, determinism - and the overlay boundary: stored positions win
/// for IRI-derived ids and never apply to expression nodes (owl-diagram Requirements 2.1, 3.2, 4.1).
/// </summary>
public class OwlLayoutTests
{
    private const string Prelude = """
        @prefix : <http://example.org/t#> .
        @prefix owl: <http://www.w3.org/2002/07/owl#> .
        @prefix rdfs: <http://www.w3.org/2000/01/rdf-schema#> .

        """;

    private static OwlGraphResult Graph(string body) =>
        OwlProjection.Project(RdfParser.Parse(RdfDocument.Parse(Prelude + body)));

    [Fact]
    public void Classes_FallIntoColumnsByAssertedDepth_RootsLeft()
    {
        // Arrange: a three-deep asserted chain.
        var graph = Graph("""
            :Leaf a owl:Class ; rdfs:subClassOf :Middle .
            :Middle a owl:Class ; rdfs:subClassOf :Root .
            :Root a owl:Class .
            """);

        // Act.
        var positions = OwlLayout.Positions(graph);

        // Assert: the root's column is left of its subclass, which is left of the leaf - the
        // Protégé-tree lens as geometry.
        var root = positions["res:http://example.org/t#Root"];
        var middle = positions["res:http://example.org/t#Middle"];
        var leaf = positions["res:http://example.org/t#Leaf"];
        Assert.True(root.X < middle.X);
        Assert.True(middle.X < leaf.X);
    }

    [Fact]
    public void ASubclassCycle_CollapsesToOneColumn_AndTerminates()
    {
        // Arrange: two classes asserting each other.
        var graph = Graph("""
            :A a owl:Class ; rdfs:subClassOf :B .
            :B a owl:Class ; rdfs:subClassOf :A .
            :Below a owl:Class ; rdfs:subClassOf :A .
            """);

        // Act: cyclic input must terminate, never recurse forever.
        var positions = OwlLayout.Positions(graph);

        // Assert: the cycle's members share a column; the class under the cycle sits right of it.
        Assert.Equal(positions["res:http://example.org/t#A"].X, positions["res:http://example.org/t#B"].X);
        Assert.True(positions["res:http://example.org/t#Below"].X > positions["res:http://example.org/t#A"].X);
    }

    [Fact]
    public void Satellites_StackBesideTheirOwner_AndBandsHoldTheRest()
    {
        // Arrange: an expression owner, a datatype target, an individual, and a header.
        var graph = Graph("""
            <http://example.org/t> a owl:Ontology .
            :A a owl:Class ;
                rdfs:subClassOf [ a owl:Restriction ; owl:onProperty :p ; owl:someValuesFrom :B ] .
            :B a owl:Class .
            :hasName a owl:DatatypeProperty ; rdfs:domain :A ; rdfs:range <http://www.w3.org/2001/XMLSchema#string> .
            :a1 a :A .
            """);

        // Act.
        var positions = OwlLayout.Positions(graph);

        // Assert: the header sits top-left; the expression and the datatype stack beside :A; the
        // individual bands below every class.
        Assert.Equal(new RegistrationPosition(0, 0), positions["res:http://example.org/t"]);
        var owner = positions["res:http://example.org/t#A"];
        var expression = positions.Single(entry => entry.Key.StartsWith("expr:", StringComparison.Ordinal)).Value;
        var datatype = positions["res:http://www.w3.org/2001/XMLSchema#string"];
        Assert.True(expression.X > owner.X);
        Assert.True(expression.Y > owner.Y);
        Assert.True(datatype.X > owner.X);
        var individual = positions["res:http://example.org/t#a1"];
        var lowestClass = new[] { owner, positions["res:http://example.org/t#B"] }.Max(p => p.Y);
        Assert.True(individual.Y > lowestClass);

        // Every drawn node has a place.
        Assert.All(graph.Nodes, node => Assert.True(positions.ContainsKey(node.Id)));
    }

    [Fact]
    public void TheLayout_IsDeterministic()
    {
        // Arrange.
        const string body = """
            :A a owl:Class ; rdfs:subClassOf :B .
            :B a owl:Class .
            :C a owl:Class ; rdfs:subClassOf [ a owl:Restriction ; owl:onProperty :p ; owl:someValuesFrom :A ] .
            """;

        // Act: the same graph twice.
        var first = OwlLayout.Positions(Graph(body));
        var second = OwlLayout.Positions(Graph(body));

        // Assert: same ids, same geometry - no physics, no randomness.
        Assert.Equal(first.Count, second.Count);
        Assert.All(first, entry => Assert.Equal(entry.Value, second[entry.Key]));
    }

    [Fact]
    public void TheOverlay_WinsForIriDerivedIds_AndNeverAppliesToExpressions()
    {
        // Arrange.
        var graph = Graph("""
            :A a owl:Class ;
                rdfs:subClassOf [ a owl:Restriction ; owl:onProperty :p ; owl:someValuesFrom :B ] .
            :B a owl:Class .
            """);
        var computed = OwlLayout.Positions(graph);
        var expressionId = computed.Keys.Single(id => id.StartsWith("expr:", StringComparison.Ordinal));
        var stored = new Dictionary<string, RegistrationPosition>(StringComparer.Ordinal)
        {
            ["res:http://example.org/t#A"] = new(999, 888),
            [expressionId] = new(111, 222),
        };

        // Act.
        var merged = OwlLayout.Apply(computed, stored);

        // Assert: the class takes its authored position; the expression keeps its computed place,
        // because its id does not survive an edit - the identity boundary's reason.
        Assert.Equal(new RegistrationPosition(999, 888), merged["res:http://example.org/t#A"]);
        Assert.Equal(computed[expressionId], merged[expressionId]);
        Assert.False(OwlLayout.IsPositionable(expressionId));
        Assert.True(OwlLayout.IsPositionable("res:http://example.org/t#A"));
        Assert.True(OwlLayout.IsPositionable("ind:http://example.org/t#A"));
    }
}
