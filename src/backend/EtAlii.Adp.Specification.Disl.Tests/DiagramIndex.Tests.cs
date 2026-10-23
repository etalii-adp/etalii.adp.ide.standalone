using Xunit;

namespace EtAlii.Adp.Specification.Disl.Tests;

/// <summary>
/// What a diagram keeps of its elements between two changes: the elements of each type and the element of each id
/// (DISL §12.2, <c>diagram.nodesOfType</c>, <c>diagram.relationsOfType</c> and <c>diagram.elementById</c>). An id
/// expression or an order that reads them once per element would otherwise make a list, or walk every element, each
/// time, which costs the square of the model's size.
/// </summary>
public class DiagramIndexTests
{
    private static readonly DislSpecification Specification = Specifications.Loaded(Specifications.With("""
        "metamodel": {
          "types": { "Station": {}, "Halt": { "extends": "Station" }, "Depot": {} },
          "relations": {
            "Track": { "source": "Station", "target": "Station" } } }
        """));

    [Fact]
    public void TheNodesOfAType_AreOneList_UntilTheDiagramChanges()
    {
        // Arrange.
        var diagram = new DislDiagram(Specification);
        diagram.AddNode("Station", "a");
        diagram.AddNode("Depot", "d");

        // Act.
        var first = diagram.NodesOfType("Station");
        var second = diagram.NodesOfType("Station");

        // Assert.
        Assert.Same(first, second);
    }

    [Fact]
    public void TheRelationsOfAType_AndTheElements_AreOneList_UntilTheDiagramChanges()
    {
        // Arrange.
        var diagram = new DislDiagram(Specification);
        var a = diagram.AddNode("Station", "a");
        var b = diagram.AddNode("Station", "b");
        diagram.AddRelation("Track", "t", a, b);

        // Act and assert.
        Assert.Same(diagram.RelationsOfType("Track"), diagram.RelationsOfType("Track"));
        Assert.Same(diagram.Elements, diagram.Elements);
    }

    [Fact]
    public void AnAddedNode_JoinsTheListsOfItsTypes_AndAListHandedOutBefore_StaysAsItWas()
    {
        // Arrange.
        var diagram = new DislDiagram(Specification);
        diagram.AddNode("Station", "a");
        var stations = diagram.NodesOfType("Station");
        var depots = diagram.NodesOfType("Depot");
        var elements = diagram.Elements;

        // Act.
        diagram.AddNode("Halt", "h");

        // Assert.
        Assert.Equal(["a"], stations.Select(node => node.Id));
        Assert.Equal(["a", "h"], diagram.NodesOfType("Station").Select(node => node.Id));
        Assert.Same(depots, diagram.NodesOfType("Depot"));
        Assert.Equal(["a"], elements.Select(element => element.Id));
        Assert.Equal(["a", "h"], diagram.Elements.Select(element => element.Id));
    }

    [Fact]
    public void AnElementAddedAfterALookup_IsFoundByItsId()
    {
        // Arrange.
        var diagram = new DislDiagram(Specification);
        diagram.AddNode("Station", "a");
        Assert.Null(diagram.ElementById("b"));

        // Act.
        var b = diagram.AddNode("Station", "b");

        // Assert.
        Assert.Same(b, diagram.ElementById("b"));
    }

    [Fact]
    public void AnIdGivenAfterALookup_IsFoundByItsNewId_AndNotByItsOld()
    {
        // Arrange: an id rule gives an element its id after the model is built (§11.5.2).
        var diagram = new DislDiagram(Specification);
        var a = diagram.AddNode("Station", "a");
        Assert.Same(a, diagram.ElementById("a"));

        // Act.
        a.Id = "station:a";

        // Assert.
        Assert.Null(diagram.ElementById("a"));
        Assert.Same(a, diagram.ElementById("station:a"));
    }

    [Fact]
    public void TwoElementsWithOneId_TheFirstInModelOrderIsFound_ANodeBeforeARelation()
    {
        // Arrange: the relation is added first, and still the node comes first in model order.
        var diagram = new DislDiagram(Specification);
        var a = diagram.AddNode("Station", "a");
        var b = diagram.AddNode("Station", "b");
        Assert.Same(a, diagram.ElementById("a"));
        var track = diagram.AddRelation("Track", "x", a, b);
        Assert.Same(track, diagram.ElementById("x"));

        // Act.
        var node = diagram.AddNode("Depot", "x");
        diagram.AddNode("Depot", "a");

        // Assert.
        Assert.Same(node, diagram.ElementById("x"));
        Assert.Same(a, diagram.ElementById("a"));
    }

    [Fact]
    public void ARemovedElement_IsNotFound_AndLeavesTheListsOfItsTypes()
    {
        // Arrange.
        var diagram = new DislDiagram(Specification);
        var a = diagram.AddNode("Station", "a");
        diagram.AddNode("Station", "b");
        Assert.Same(a, diagram.ElementById("a"));
        Assert.Equal(2, diagram.NodesOfType("Station").Count);

        // Act.
        diagram.Remove(a);

        // Assert.
        Assert.Null(diagram.ElementById("a"));
        Assert.Equal(["b"], diagram.NodesOfType("Station").Select(node => node.Id));
    }

    [Fact]
    public void ARetypedNode_MovesToTheListsOfItsNewType()
    {
        // Arrange.
        var diagram = new DislDiagram(Specification);
        var a = diagram.AddNode("Station", "a");
        Assert.Single(diagram.NodesOfType("Station"));
        Assert.Empty(diagram.NodesOfType("Depot"));

        // Act.
        a.Retype(Specification.Metamodel.TypeOf("Depot")!, new Dictionary<string, object?>());

        // Assert.
        Assert.Empty(diagram.NodesOfType("Station"));
        Assert.Same(a, Assert.Single(diagram.NodesOfType("Depot")));
    }
}
