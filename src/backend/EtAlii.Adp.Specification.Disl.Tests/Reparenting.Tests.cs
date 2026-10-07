using EtAlii.Adp.Specification.Fbl.Planning;
using Xunit;

namespace EtAlii.Adp.Specification.Disl.Tests;

/// <summary>
/// A <c>reparent</c> action (DISL §9.4), applied to the working state and written as an FBL move
/// (runtime plan step S16): the node moves with everything beneath it, to the place its <c>after</c>
/// or <c>before</c> names, or last, and the change records that place as an index counted before the move.
/// </summary>
public class ReparentingTests
{
    private static readonly DislSpecification Specification = Specifications.Loaded(Specifications.With("""
        "metamodel": { "types": { "Box": { "attributes": { "name": { "type": "string" } }, "children": { "allowed": ["Box"] } } } },
        "behavior": {
          "operations": {
            "last": { "label": "Last", "for": ["Box"], "actions": [
              { "reparent": { "parent": "diagram.elementById('p')" } },
              { "set": { "name": "string(size(self.parent.children)) + ' under ' + self.parent.id" } } ] },
            "afterP1": { "label": "After", "for": ["Box"], "actions": [ { "reparent": { "parent": "diagram.elementById('p')", "after": "diagram.elementById('p1')" } } ] },
            "beforeP1": { "label": "Before", "for": ["Box"], "actions": [ { "reparent": { "parent": "diagram.elementById('p')", "before": "diagram.elementById('p1')" } } ] },
            "top": { "label": "Top", "for": ["Box"], "actions": [ { "reparent": { "parent": "null" } } ] },
            "itself": { "label": "Itself", "for": ["Box"], "actions": [ { "reparent": { "parent": "self" } } ] },
            "beside": { "label": "Beside", "for": ["Box"], "actions": [ { "reparent": { "parent": "diagram.elementById('p')", "after": "diagram.elementById('q')" } } ] }
          }
        }
        """));

    /// <summary>p (p1, p2) and q (q1 (q1a)), in document order.</summary>
    private static DislDiagram Tree()
    {
        var diagram = new DislDiagram(Specification);
        var p = diagram.AddNode("Box", "p");
        diagram.AddNode("Box", "p1", null, p, "children");
        diagram.AddNode("Box", "p2", null, p, "children");
        var q = diagram.AddNode("Box", "q");
        var q1 = diagram.AddNode("Box", "q1", null, q, "children");
        diagram.AddNode("Box", "q1a", null, q1, "children");
        return diagram;
    }

    private static string Order(DislDiagram diagram) =>
        string.Join(" ", diagram.Nodes.Select(node => node.Parent is null ? node.Id : $"{node.Parent.Id}/{node.Id}"));

    [Theory]
    [InlineData("last", -1, "p p/p1 p/p2 p/q1 q1/q1a q")]
    [InlineData("afterP1", 1, "p p/p1 p/q1 q1/q1a p/p2 q")]
    [InlineData("beforeP1", 0, "p p/q1 q1/q1a p/p1 p/p2 q")]
    public void AReparent_MovesTheSubtree_ToItsPlace_AndRecordsTheIndex(string operation, int index, string order)
    {
        // Arrange.
        var diagram = Tree();

        // Act.
        var transaction = OperationInterpreter.Run(Specification, operation, diagram, diagram.ElementById("q1"), DislIds.Fixed());

        // Assert.
        Assert.True(transaction.WasApplied, transaction.Refusal);
        var reparent = Assert.IsType<DislChange.Reparent>(transaction.Changes[0]);
        Assert.Equal(("q1", "p", index), (reparent.ElementId, reparent.ParentId, reparent.Index));
        Assert.Equal(order, Order(diagram));
        Assert.Empty(diagram.ElementById("q")!.Children);
    }

    [Fact]
    public void AnActionAfterAReparent_SeesTheMove()
    {
        // Arrange.
        var diagram = Tree();

        // Act.
        var transaction = OperationInterpreter.Run(Specification, "last", diagram, diagram.ElementById("q1"), DislIds.Fixed());

        // Assert.
        var set = Assert.IsType<DislChange.Set>(transaction.Changes[1]);
        Assert.Equal("3 under p", set.Attributes["name"]);
    }

    [Fact]
    public void AReparentToTheTopLevel_PutsTheNodeLast()
    {
        // Arrange.
        var diagram = Tree();

        // Act.
        var transaction = OperationInterpreter.Run(Specification, "top", diagram, diagram.ElementById("p1"), DislIds.Fixed());

        // Assert.
        Assert.True(transaction.WasApplied, transaction.Refusal);
        Assert.Equal("p p/p2 q q/q1 q1/q1a p1", Order(diagram));
        Assert.Equal(new ModelChange.Move("p1", null, -1), DislWrite.ToFbl(Specification, transaction.Changes[0]));
    }

    [Theory]
    [InlineData("itself", "q1", "A node cannot move beneath itself.")]
    [InlineData("last", "p", "A node cannot move beneath itself.")]
    [InlineData("beside", "q1", "places the element beside one that is not beneath its new parent")]
    public void AReparentThatCannotBe_RefusesTheTransaction(string operation, string self, string reason)
    {
        // Arrange.
        var diagram = Tree();

        // Act.
        var transaction = OperationInterpreter.Run(Specification, operation, diagram, diagram.ElementById(self), DislIds.Fixed());

        // Assert.
        Assert.False(transaction.WasApplied);
        Assert.Contains(reason, transaction.Refusal, StringComparison.Ordinal);
    }

    [Fact]
    public void AReparent_IsWrittenAsAMove_ToItsParentAndIndex()
    {
        // Act.
        var change = DislWrite.ToFbl(Specification, new DislChange.Reparent("q1", "p", "children", "p1", null, 1));

        // Assert.
        Assert.Equal(new ModelChange.Move("q1", "p", 1), change);
    }
}
