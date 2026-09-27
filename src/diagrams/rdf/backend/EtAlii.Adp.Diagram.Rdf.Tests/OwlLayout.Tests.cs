using EtAlii.Adp.Documents;
using EtAlii.Adp.Hierarchy;
using Xunit;
using IoPath = System.IO.Path;

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
        OwlProjection.Project(RdfParser.Parse(LineDocument.Parse(Prelude + body)));

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

    [Fact]
    public void NoTwoElementsOverlap_OnARealOntology()
    {
        // The guard behind "this looks chaotic": the first drawing of OWL-Time put expression
        // satellites on top of the next column and stacked them through the class below, because
        // a class occupied a fixed row while its satellites needed more. Measured on the real
        // vendored ontology - 114 elements, 55 of them expressions - because that is the file the
        // collisions showed up in (owl-diagram Requirement 2.1).

        // Arrange.
        var graph = OwlProjection.Project(VendoredOntology());

        // Act.
        var positions = OwlLayout.Positions(graph);

        // Assert: every drawn element has a place, and no two boxes intersect.
        Assert.All(graph.Nodes, node => Assert.True(positions.ContainsKey(node.Id), $"{node.Id} was not placed"));

        var boxes = graph.Nodes
            .Select(node =>
            {
                var (width, height) = OwlLayout.SizeOf(node);
                var position = positions[node.Id];
                return (node.Id, Left: position.X, Top: position.Y, Right: position.X + width, Bottom: position.Y + height);
            })
            .ToList();

        var collisions = new List<string>();
        for (var i = 0; i < boxes.Count; i++)
        {
            for (var j = i + 1; j < boxes.Count; j++)
            {
                var (aId, aLeft, aTop, aRight, aBottom) = boxes[i];
                var (bId, bLeft, bTop, bRight, bBottom) = boxes[j];
                if (aLeft < bRight && bLeft < aRight && aTop < bBottom && bTop < aBottom)
                {
                    collisions.Add($"{aId} overlaps {bId}");
                }
            }
        }

        Assert.True(collisions.Count == 0, string.Join("; ", collisions.Take(8)));
    }

    [Fact]
    public void ASubclassEdge_RunsBetweenNeighbouringColumns_RatherThanAcrossTheCanvas()
    {
        // The other half of the same complaint: crossings. Ordering each column by the barycentre
        // of its parents' rows is what keeps a hierarchy edge short, so this measures the drop -
        // the average vertical distance a subclass edge spans - on the real ontology.

        // Arrange.
        var graph = OwlProjection.Project(VendoredOntology());
        var positions = OwlLayout.Positions(graph);

        // Act.
        var drops = graph.Edges
            .Where(edge => edge.Kind == OwlEdgeKind.Subclass
                && positions.ContainsKey(edge.FromId) && positions.ContainsKey(edge.ToId))
            .Select(edge => Math.Abs(positions[edge.FromId].Y - positions[edge.ToId].Y))
            .ToList();

        // Assert: a hierarchy edge stays local. Without the barycentre pass the same corpus
        // averages far more, because a column's order has nothing to do with its parents'.
        Assert.NotEmpty(drops);
        Assert.True(drops.Average() < 900, $"subclass edges average a {drops.Average():F0} unit drop");
    }

    /// <summary>The vendored OWL-Time ontology - the real corpus these two guards measure.</summary>
    private static RdfModel VendoredOntology()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null
            && !File.Exists(IoPath.Combine(directory.FullName, "src", "diagrams", "rdf", "examples", "owl-time", "owl-time.ttl")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        var path = IoPath.Combine(directory.FullName, "src", "diagrams", "rdf", "examples", "owl-time", "owl-time.ttl");
        return RdfParser.Parse(LineDocument.Parse(File.ReadAllText(path)));
    }
}
