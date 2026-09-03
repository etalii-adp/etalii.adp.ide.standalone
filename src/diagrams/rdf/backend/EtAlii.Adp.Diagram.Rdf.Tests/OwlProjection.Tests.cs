using System.Text;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.Rdf.Tests;

/// <summary>
/// The ontology reading's projection: OWL's vocabulary over the family model - classes,
/// properties as edges, expression nodes on structural ids, punning, anchors, and the
/// neighborhood-whole budget cut (owl-diagram Requirements 1-3 and 8.3).
/// </summary>
public class OwlProjectionTests
{
    private const string Ns = "http://example.org/pizza#";

    private static OwlGraphResult ProjectFixture(string name)
    {
        var text = File.ReadAllText(IoPath.Combine(AppContext.BaseDirectory, "Fixtures", name));
        return OwlProjection.Project(RdfParser.Parse(RdfDocument.Parse(text)));
    }

    [Fact]
    public void TheOntologyFixture_ProjectsTheAdoptedVocabulary()
    {
        // Arrange & act.
        var graph = ProjectFixture("owl-ontology.ttl");

        // Assert.
        // The ontology header draws once, labeled, carrying its version and its imports as rows -
        // imports named, never fetched (Requirement 1.5).
        var header = graph.Nodes.Single(node => node.Kind == OwlNodeKind.OntologyHeader);
        Assert.Equal("Pizza ontology", header.Display);
        Assert.Contains(header.Rows, row => row is { Predicate: "owl:imports", Value: "http://example.org/base" });
        Assert.Contains(header.Rows, row => row is { Predicate: "owl:versionInfo", Value: "1.0" });

        // Classes draw as class nodes, rdfs:label preferred over the prefixed name (Requirement 1.4).
        var pizza = graph.Nodes.Single(node => node.Id == $"res:{Ns}Pizza");
        Assert.Equal(OwlNodeKind.Class, pizza.Kind);
        Assert.Equal("Pizza", pizza.Display);

        // Punning: the same IRI's individual role draws in its own place (Requirement 2.4).
        Assert.Contains(graph.Nodes, node => node.Id == $"ind:{Ns}Pizza" && node.Kind == OwlNodeKind.Individual);

        // The axiom edges (Requirement 2.1, 2.2).
        Assert.Contains(graph.Edges, edge =>
            edge is { Kind: OwlEdgeKind.Subclass } && edge.FromId == $"res:{Ns}Vegetarian" && edge.ToId == $"res:{Ns}Pizza");
        Assert.Contains(graph.Edges, edge =>
            edge is { Kind: OwlEdgeKind.Equivalent } && edge.FromId == $"res:{Ns}Vegetarian" && edge.ToId == $"res:{Ns}VeggiePizza");
        Assert.Contains(graph.Edges, edge =>
            edge is { Kind: OwlEdgeKind.Disjoint } && edge.FromId == $"res:{Ns}VegetarianTopping" && edge.ToId == $"res:{Ns}MeatTopping");

        // The AllDisjointClasses group becomes pairwise disjointness (Requirement 2.2).
        Assert.Contains(graph.Edges, edge =>
            edge is { Kind: OwlEdgeKind.Disjoint } && edge.FromId == $"res:{Ns}Pizza" && edge.ToId == $"res:{Ns}Topping");

        // An object property is an edge between its domain and range, characteristics and the
        // inverse folded into its label, never extra nodes (Requirements 1.1, 2.3).
        var hasTopping = graph.Edges.Single(edge => edge.Kind == OwlEdgeKind.ObjectProperty && edge.PropertyIri == $"{Ns}hasTopping");
        Assert.Equal($"res:{Ns}Pizza", hasTopping.FromId);
        Assert.Equal($"res:{Ns}Topping", hasTopping.ToId);
        Assert.Contains("inverse functional", hasTopping.Label);
        Assert.Contains("inverse of", hasTopping.Label);

        // A property without stated ends anchors at its own materialized owl:Thing pair (Requirement 1.2).
        Assert.Contains(graph.Nodes, node => node.Id == $"thing:{Ns}toppingOf|domain" && node.Kind == OwlNodeKind.Thing);
        Assert.Contains(graph.Edges, edge =>
            edge.PropertyIri == $"{Ns}toppingOf" && edge.FromId == $"thing:{Ns}toppingOf|domain" && edge.ToId == $"thing:{Ns}toppingOf|range");

        // A datatype property reaches a datatype node - the schema half of the literal-node
        // position (Requirement 1.1) - and one with no range reaches a materialized Literal.
        var xsdInteger = graph.Nodes.Single(node => node.Id == "res:http://www.w3.org/2001/XMLSchema#integer");
        Assert.Equal(OwlNodeKind.Datatype, xsdInteger.Kind);
        Assert.Contains(graph.Edges, edge =>
            edge is { Kind: OwlEdgeKind.DatatypeProperty } && edge.FromId == $"res:{Ns}Pizza" && edge.ToId == xsdInteger.Id);
        Assert.Contains(graph.Nodes, node => node.Id == $"dt:{Ns}hasNote" && node is { Kind: OwlNodeKind.Datatype, Display: "Literal" });

        // Individuals are cards: types as badges, literal assertions as rows, object assertions
        // as edges - the data half of the literal-node position (Requirement 1.3).
        var margherita = graph.Nodes.Single(node => node.Id == $"res:{Ns}Margherita");
        Assert.Equal(OwlNodeKind.Individual, margherita.Kind);
        Assert.Contains("Pizza", margherita.Badges);
        Assert.Contains(margherita.Rows, row => row is { Predicate: ":hasCalories", Value: "850" });
        Assert.Contains(graph.Edges, edge =>
            edge is { Kind: OwlEdgeKind.Assertion } && edge.FromId == margherita.Id && edge.ToId == $"res:{Ns}Mozzarella");

        // Deprecation dims; an IRI used here but declared nowhere is external (Requirement 1.6).
        Assert.True(graph.Nodes.Single(node => node.Id == $"res:{Ns}MeatTopping").Deprecated);
        Assert.True(graph.Nodes.Single(node => node.Id == "res:http://example.org/external#Base").External);
        Assert.False(pizza.External);

        // Nothing was cut.
        Assert.False(graph.Truncated);
    }

    [Fact]
    public void ClassExpressions_DrawOnStructuralIds_AttachedToTheirOwner()
    {
        // Arrange & act.
        var graph = ProjectFixture("owl-ontology.ttl");

        // Assert.
        // Vegetarian's blank restriction is its SECOND subClassOf axiom, so the structural
        // ordinal is 1 - the ordinal counts the owner's same-predicate axioms in document order.
        var restrictionId = $"expr:{Ns}Vegetarian|http://www.w3.org/2000/01/rdf-schema#subClassOf|1";
        var restriction = graph.Nodes.Single(node => node.Id == restrictionId);
        Assert.Equal(OwlNodeKind.Restriction, restriction.Kind);
        Assert.Equal($"res:{Ns}Vegetarian", restriction.OwnerId);
        Assert.False(restriction.Malformed);
        Assert.NotNull(restriction.ExpressionRoot);

        // The axiom edge carries its kind to the expression node; the structure edge reaches the
        // named filler (Requirement 3.1).
        Assert.Contains(graph.Edges, edge =>
            edge is { Kind: OwlEdgeKind.Subclass } && edge.FromId == $"res:{Ns}Vegetarian" && edge.ToId == restrictionId);
        Assert.Contains(graph.Edges, edge =>
            edge is { Kind: OwlEdgeKind.Expression } && edge.FromId == restrictionId && edge.ToId == $"res:{Ns}VegetarianTopping");

        // Nesting: the union operator, its restriction child, and the intersection grandchild
        // each draw on child-suffixed ids wired by structure edges.
        var unionId = $"expr:{Ns}Special|http://www.w3.org/2002/07/owl#equivalentClass|0";
        Assert.Equal(OwlNodeKind.Operator, graph.Nodes.Single(node => node.Id == unionId).Kind);
        Assert.Contains(graph.Nodes, node => node.Id == $"{unionId}/0" && node.Kind == OwlNodeKind.Restriction);
        Assert.Contains(graph.Nodes, node => node.Id == $"{unionId}/0/0" && node.Kind == OwlNodeKind.Operator);
        Assert.Contains(graph.Edges, edge =>
            edge is { Kind: OwlEdgeKind.Expression } && edge.FromId == unionId && edge.ToId == $"res:{Ns}Vegetarian");
        Assert.Contains(graph.Edges, edge =>
            edge is { Kind: OwlEdgeKind.Expression } && edge.FromId == $"{unionId}/0/0" && edge.ToId == "res:http://example.org/external#Smoked");
    }

    [Fact]
    public void AMalformedRestriction_DrawsAsAMarkedProblemNode()
    {
        // Arrange & act.
        var graph = ProjectFixture("owl-malformed.ttl");

        // Assert: missing owl:onProperty marks the node rather than hiding it (Requirement 3.5).
        var expression = graph.Nodes.Single(node => node.Kind == OwlNodeKind.Restriction);
        Assert.True(expression.Malformed);
    }

    [Fact]
    public void StructuralIds_AreDeterministicWithinAParse()
    {
        // Arrange.
        var text = File.ReadAllText(IoPath.Combine(AppContext.BaseDirectory, "Fixtures", "owl-ontology.ttl"));

        // Act: the same bytes twice.
        var first = OwlProjection.Project(RdfParser.Parse(RdfDocument.Parse(text)));
        var second = OwlProjection.Project(RdfParser.Parse(RdfDocument.Parse(text)));

        // Assert: same ids, same order, same edges - the same file always opens the same way.
        Assert.Equal(first.Nodes.Select(node => node.Id), second.Nodes.Select(node => node.Id));
        Assert.Equal(first.Edges.Select(edge => edge.Id), second.Edges.Select(edge => edge.Id));
    }

    [Fact]
    public void StructuralIds_AreDeliberatelyUnstableAcrossAnInsertedAxiom()
    {
        // This test keeps the blank-node identity boundary's stated reason TRUE IN CODE: the
        // boundary refuses stored positions and element-keyed edits on expression nodes because
        // their ids do not survive an edit. If this test ever fails, the ids have become stable
        // and the boundary's reason - and possibly the boundary - should be revisited.

        // Arrange: the same ontology, with one more subClassOf axiom for the same owner inserted
        // BEFORE the blank restriction axiom.
        const string before = """
            @prefix : <http://example.org/pizza#> .
            @prefix owl: <http://www.w3.org/2002/07/owl#> .
            @prefix rdfs: <http://www.w3.org/2000/01/rdf-schema#> .

            :Vegetarian a owl:Class ;
                rdfs:subClassOf :Pizza ;
                rdfs:subClassOf [ a owl:Restriction ; owl:onProperty :hasTopping ; owl:allValuesFrom :VegetarianTopping ] .
            """;
        const string after = """
            @prefix : <http://example.org/pizza#> .
            @prefix owl: <http://www.w3.org/2002/07/owl#> .
            @prefix rdfs: <http://www.w3.org/2000/01/rdf-schema#> .

            :Vegetarian a owl:Class ;
                rdfs:subClassOf :Pizza ;
                rdfs:subClassOf :Food ;
                rdfs:subClassOf [ a owl:Restriction ; owl:onProperty :hasTopping ; owl:allValuesFrom :VegetarianTopping ] .
            """;

        // Act.
        var beforeId = OwlProjection.Project(RdfParser.Parse(RdfDocument.Parse(before)))
            .Nodes.Single(node => node.Kind == OwlNodeKind.Restriction).Id;
        var afterId = OwlProjection.Project(RdfParser.Parse(RdfDocument.Parse(after)))
            .Nodes.Single(node => node.Kind == OwlNodeKind.Restriction).Id;

        // Assert: the restriction's id shifted with the inserted axiom.
        Assert.EndsWith("|1", beforeId, StringComparison.Ordinal);
        Assert.EndsWith("|2", afterId, StringComparison.Ordinal);
        Assert.NotEqual(beforeId, afterId);
    }

    [Fact]
    public void TheBudgetCut_KeepsClassNeighborhoodsWhole()
    {
        // Arrange: five classes, each owning one restriction - units of two nodes each.
        var text = new StringBuilder()
            .AppendLine("@prefix : <http://example.org/big#> .")
            .AppendLine("@prefix owl: <http://www.w3.org/2002/07/owl#> .")
            .AppendLine("@prefix rdfs: <http://www.w3.org/2000/01/rdf-schema#> .")
            .AppendLine();
        for (var i = 0; i < 5; i++)
        {
            text.AppendLine($":Class{i} a owl:Class ;");
            text.AppendLine($"    rdfs:subClassOf [ a owl:Restriction ; owl:onProperty :p ; owl:someValuesFrom :Class{i} ] .");
        }

        // Act: a budget of five holds two whole units, and must not split the third.
        var graph = OwlProjection.Project(RdfParser.Parse(RdfDocument.Parse(text.ToString())), budget: 5);

        // Assert.
        Assert.True(graph.Truncated);
        Assert.Equal(4, graph.Shown);
        Assert.Equal(10, graph.Total);

        // Every drawn expression node's owner is drawn too - no orphaned expressions, ever.
        var drawn = graph.Nodes.Select(node => node.Id).ToHashSet(StringComparer.Ordinal);
        Assert.All(
            graph.Nodes.Where(node => node.OwnerId.Length > 0),
            node => Assert.Contains(node.OwnerId, drawn));
        Assert.All(graph.Edges, edge =>
        {
            Assert.Contains(edge.FromId, drawn);
            Assert.Contains(edge.ToId, drawn);
        });
    }
}
