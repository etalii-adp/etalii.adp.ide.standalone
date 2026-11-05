using EtAlii.Adp.Specification.Cel;
using Xunit;

namespace EtAlii.Adp.Specification.Disl.Tests;

/// <summary>
/// <c>reachable(relType)</c>, <c>inCycle(relType)</c> and <c>predecessors(relType)</c> on an element (DISL §12.2, §12.4):
/// the transitive successors over relations of a type and its subtypes, <c>self</c> among them only when it is on a cycle;
/// whether it is; and the elements one such relation leads from to this one. An element downstream of a cycle, reached
/// from one of its members, is not on it.
/// </summary>
public class DislReachabilityTests
{
    private static readonly DislSpecification Specification = Specifications.Loaded(Specifications.With("""
        "metamodel": {
          "types": { "Node": {} },
          "relations": {
            "Edge": { "abstract": true, "source": "Node", "target": "Node", "allowSelfLoops": true },
            "Flow": { "extends": "Edge" },
            "Back": { "extends": "Edge" } } }
        """));

    private readonly DislDiagram _diagram = new(Specification);

    public DislReachabilityTests()
    {
        // A self-loop: s waits for itself.
        var s = Node("s");
        _diagram.AddRelation("Flow", "s-s", s, s);

        // A chain, closed into a cycle only by a relation of the sibling type: a, b, c.
        var a = Node("a");
        var b = Node("b");
        var c = Node("c");
        _diagram.AddRelation("Flow", "a-b", a, b);
        _diagram.AddRelation("Flow", "b-c", b, c);
        _diagram.AddRelation("Back", "c-a", c, a);

        // Two cycles that share a member: x and y, and y and z.
        var x = Node("x");
        var y = Node("y");
        var z = Node("z");
        _diagram.AddRelation("Flow", "x-y", x, y);
        _diagram.AddRelation("Flow", "y-x", y, x);
        _diagram.AddRelation("Flow", "y-z", y, z);
        _diagram.AddRelation("Flow", "z-y", z, y);

        // Downstream of those cycles and on none: d waits for z, and e for d.
        var d = Node("d");
        var e = Node("e");
        _diagram.AddRelation("Flow", "z-d", z, d);
        _diagram.AddRelation("Flow", "d-e", d, e);

        // Outside every relation.
        Node("w");
    }

    private DislElement Node(string id) => _diagram.AddNode("Node", id);

    private object? Evaluate(string id, string expression) =>
        Specification.Environment(DislContext.Element).Compile(expression)
            .Evaluate(new Dictionary<string, object?> { ["self"] = _diagram.ElementById(id), ["diagram"] = _diagram, ["env"] = new CelMap() });

    [Theory]
    [InlineData("s", "['s']")]
    [InlineData("a", "['b', 'c']")]
    [InlineData("b", "['c']")]
    [InlineData("c", "[]")]
    [InlineData("x", "['d', 'e', 'x', 'y', 'z']")]
    [InlineData("y", "['d', 'e', 'x', 'y', 'z']")]
    [InlineData("z", "['d', 'e', 'x', 'y', 'z']")]
    [InlineData("d", "['e']")]
    [InlineData("e", "[]")]
    [InlineData("w", "[]")]
    public void Reachable_IsTheTransitiveSuccessors_SelfOnlyOnACycle(string id, string expected) =>
        Assert.Equal(true, Evaluate(id, $"self.reachable('Flow').map(e, e.id).sort() == {expected}"));

    [Theory]
    [InlineData("s", true)]
    [InlineData("a", false)]
    [InlineData("c", false)]
    [InlineData("x", true)]
    [InlineData("y", true)]
    [InlineData("z", true)]
    [InlineData("d", false)]
    [InlineData("e", false)]
    [InlineData("w", false)]
    public void InCycle_HoldsForAnElementOnACycle(string id, bool expected) =>
        Assert.Equal(expected, Evaluate(id, "self.inCycle('Flow')"));

    [Theory]
    [InlineData("s", "['s']")]
    [InlineData("a", "[]")]
    [InlineData("b", "['a']")]
    [InlineData("y", "['x', 'z']")]
    [InlineData("x", "['y']")]
    [InlineData("d", "['z']")]
    [InlineData("w", "[]")]
    public void Predecessors_AreTheSourcesOfTheIncomingRelations_InModelOrder(string id, string expected) =>
        Assert.Equal(true, Evaluate(id, $"self.predecessors('Flow').map(e, e.id) == {expected}"));

    [Fact]
    public void Predecessors_NameEachSourceOnce()
    {
        // Arrange: a second relation from x to y, of the sibling type.
        _diagram.AddRelation("Back", "x-y-again", _diagram.ElementById("x")!, _diagram.ElementById("y")!);

        // Act and assert.
        Assert.Equal(true, Evaluate("y", "self.predecessors('Edge').map(e, e.id) == ['x', 'z']"));
    }

    [Theory]
    [InlineData("a", "self.predecessors('Edge').map(e, e.id) == ['c']", true)]
    [InlineData("a", "self.inCycle('Edge')", true)]
    [InlineData("a", "self.reachable('Edge').map(e, e.id).sort() == ['a', 'b', 'c']", true)]
    [InlineData("c", "self.reachable('Back').map(e, e.id) == ['a']", true)]
    [InlineData("c", "self.inCycle('Back')", false)]
    public void ARelationType_IncludesItsSubtypes(string id, string expression, bool expected) =>
        Assert.Equal(expected, Evaluate(id, expression));
}
