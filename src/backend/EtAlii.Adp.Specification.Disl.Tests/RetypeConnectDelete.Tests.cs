using EtAlii.Adp.Specification.Fbl.Planning;
using Xunit;

namespace EtAlii.Adp.Specification.Disl.Tests;

/// <summary>
/// A node's type changed through <c>behavior.retype</c>, a line of a derived relation drawn through its
/// <c>edits.connect</c> operation, and a deletion that leaves derived relations and nested children to
/// the removal that takes them (runtime plan step S19b).
/// </summary>
public class RetypeConnectDeleteTests
{
    private static readonly DislSpecification Specification = Specifications.Loaded(Specifications.With("""
        "metamodel": {
          "types": {
            "Box": { "attributes": { "name": { "type": "string" } }, "children": { "allowed": ["Box"] } },
            "Bin": { "extends": ["Box"], "attributes": { "size": { "type": "int" } } },
            "Bag": { "extends": ["Box"] },
            "Lid": { "attributes": { "name": { "type": "string" } } }
          },
          "relations": {
            "Holds": { "source": "Box", "target": "Box", "derived": {
              "from": "diagram.nodes.filter(n, n.parent != null)", "id": "'holds:' + item.id", "source": "item.parent", "target": "item",
              "edits": { "connect": "putUnder" } } },
            "Sees": { "source": "Box", "target": "Box", "derived": {
              "from": "diagram.nodes.filter(n, n.parent != null)", "id": "'sees:' + item.id", "source": "item.parent", "target": "item",
              "reason": "Seeing follows from holding." } }
          }
        },
        "behavior": {
          "operations": {
            "putUnder": { "label": "Put under", "for": ["Holds"], "actions": [
              { "if": "self.source.id == self.target.id", "then": [ { "abort": { "message": "'Not under itself.'" } } ] },
              { "reparent": { "target": "self.target", "parent": "self.source" } } ] }
          },
          "deletion": { "Box": { "children": "delete", "relations": "delete" } },
          "retype": {
            "Box": { "to": ["Bin"], "attributeMapping": { "size": "has(old.size) ? old.size + 1 : 3", "missing": "1 / 0" } },
            "Bin": { "to": ["Box", "Bag"], "attributeMapping": { "size": "old.size * 2" } }
          }
        }
        """));

    /// <summary>p (p1, p2 (p2a)) and q, as a reader completes them, the derived lines computed.</summary>
    private static DislDiagram Tree()
    {
        var diagram = new DislDiagram(Specification);
        var p = diagram.AddNode("Box", "p");
        diagram.AddNode("Box", "p1", null, p, "children");
        var p2 = diagram.AddNode("Bin", "p2", new Dictionary<string, object?> { ["size"] = 5L }, p, "children");
        diagram.AddNode("Box", "p2a", null, p2, "children");
        diagram.AddNode("Box", "q");
        DislModelBuilder.Complete(diagram);
        return diagram;
    }

    [Fact]
    public void ARetype_CarriesTheMappedValues_OfTheNewTypesAttributesOnly()
    {
        // Arrange.
        var diagram = Tree();

        // Act.
        var transaction = RetypePolicy.Change(Specification, diagram.ElementById("q")!, "Bin");

        // Assert.
        Assert.True(transaction.WasApplied, transaction.Refusal);
        var retype = Assert.IsType<DislChange.Retype>(Assert.Single(transaction.Changes));
        Assert.Equal(("q", "Bin"), (retype.ElementId, retype.Type));
        Assert.Equal(3L, Assert.Single(retype.Attributes, attribute => attribute.Key == "size").Value);
        Assert.Single(retype.Attributes);
    }

    [Fact]
    public void ARetype_ReadsTheOldElement()
    {
        // Arrange.
        var diagram = Tree();

        // Act.
        var transaction = RetypePolicy.Change(Specification, diagram.ElementById("p2")!, "Box");
        var bag = RetypePolicy.Change(Specification, diagram.ElementById("p2")!, "Bag");

        // Assert: Box declares no size, so nothing is mapped; a Bag inherits Box's attributes, and has none of its own.
        Assert.Empty(Assert.IsType<DislChange.Retype>(Assert.Single(transaction.Changes)).Attributes);
        Assert.Empty(Assert.IsType<DislChange.Retype>(Assert.Single(bag.Changes)).Attributes);
    }

    [Theory]
    [InlineData("p1", "Bag", "A Box cannot become a Bag.")]
    [InlineData("p1", "Lid", "A Box cannot become a Lid.")]
    [InlineData("p1", "Crate", "A Box cannot become a Crate: that is no type of node.")]
    [InlineData("p1", "Holds", "A Box cannot become a Holds: that is no type of node.")]
    public void ARetypeNotDeclared_IsRefused(string id, string type, string reason)
    {
        // Arrange.
        var diagram = Tree();

        // Act.
        var transaction = RetypePolicy.Change(Specification, diagram.ElementById(id)!, type);

        // Assert.
        Assert.Equal(reason, transaction.Refusal);
    }

    [Fact]
    public void ARetype_IsWrittenAsAnFblRetype()
    {
        // Act.
        var change = DislWrite.ToFbl(Specification, new DislChange.Retype("q", "Bin", new Dictionary<string, object?> { ["size"] = 3L }));

        // Assert.
        var retype = Assert.IsType<ModelChange.Retype>(change);
        Assert.Equal(("q", "Bin", 3L), (retype.Id, retype.Type, retype.Attributes["size"]));
    }

    [Fact]
    public void AConnect_RunsTheDerivedRelationsOperation_WithItsEndsAsSelf()
    {
        // Arrange.
        var diagram = Tree();

        // Act.
        var transaction = OperationInterpreter.Connect(Specification, "Holds", diagram, diagram.ElementById("q")!, diagram.ElementById("p1")!, DislIds.Fixed());

        // Assert.
        Assert.True(transaction.WasApplied, transaction.Refusal);
        var reparent = Assert.IsType<DislChange.Reparent>(Assert.Single(transaction.Changes));
        Assert.Equal(("p1", "q", -1), (reparent.ElementId, reparent.ParentId, reparent.Index));
    }

    [Fact]
    public void AConnect_IsRefusedByTheOperationsAbort()
    {
        // Arrange.
        var diagram = Tree();
        var q = diagram.ElementById("q")!;

        // Act.
        var transaction = OperationInterpreter.Connect(Specification, "Holds", diagram, q, q, DislIds.Fixed());

        // Assert.
        Assert.Equal("Not under itself.", transaction.Refusal);
    }

    [Theory]
    [InlineData("Sees", "Seeing follows from holding.")]
    [InlineData("Lid", "There is no relation type 'Lid'.")]
    public void AConnect_WithoutAConnectEdit_IsRefused(string relation, string reason)
    {
        // Arrange.
        var diagram = Tree();

        // Act.
        var transaction = OperationInterpreter.Connect(Specification, relation, diagram, diagram.ElementById("q")!, diagram.ElementById("p1")!, DislIds.Fixed());

        // Assert.
        Assert.Equal(reason, transaction.Refusal);
    }

    [Theory]
    [InlineData(false, "remove p2a\nremove p2")]
    [InlineData(true, "remove p2")]
    public void ADeletion_LeavesTheDerivedLines_AndNestedChildren_ToTheRemovalThatTakesThem(bool nested, string expected)
    {
        // Arrange.
        var diagram = Tree();

        // Act.
        var transaction = DeletionPolicy.Changes(Specification, diagram.ElementById("p2")!, nested);

        // Assert.
        Assert.True(transaction.WasApplied, transaction.Refusal);
        Assert.Equal(expected, string.Join("\n", transaction.Changes.Select(change => change is DislChange.Remove remove ? $"remove {remove.ElementId}" : change.ToString())));
    }
}
