using EtAlii.Adp.Specification.Cel;
using Xunit;

namespace EtAlii.Adp.Specification.Disl.Tests;

/// <summary>The element model as DISL's CEL reads it (DISL §12.2): members, containment, relation ends, type queries.</summary>
public class DislElementTests
{
    private static readonly DislSpecification Specification = Specifications.Loaded(Specifications.With("""
        "metamodel": {
          "diagram": { "attributes": { "unit": { "type": "string", "default": "month" }, "title": { "type": "string" } } },
          "types": {
            "Shape": { "abstract": true, "labelAttribute": "name", "attributes": {
              "name": { "type": "string" }, "size": { "type": "int", "default": 3 }, "weight": { "type": "number" },
              "when": { "type": "yearMonth" }, "tags": { "type": "string", "many": true }, "buddy": { "type": "Shape" } } },
            "Box": { "extends": "Shape", "children": { "allowed": ["Shape"] } },
            "Circle": { "extends": "Shape" } },
          "relations": {
            "Link": { "abstract": true, "source": "Shape", "target": "Shape" },
            "Strong": { "extends": "Link" },
            "Weak": { "extends": "Link" } } }
        """));

    private readonly DislDiagram _diagram = new(Specification, new Dictionary<string, object?> { ["title"] = "Shapes" });
    private readonly DislElement _outer;
    private readonly DislElement _inner;
    private readonly DislElement _circle;
    private readonly DislElement _strong;
    private readonly DislElement _weak;

    public DislElementTests()
    {
        _outer = _diagram.AddNode("Box", "outer", new Dictionary<string, object?> { ["name"] = "Outer", ["when"] = 24320L, ["buddy"] = "c" });
        _inner = _diagram.AddNode("Box", "inner", new Dictionary<string, object?> { ["size"] = 7L }, _outer);
        _circle = _diagram.AddNode("Circle", "c", new Dictionary<string, object?> { ["name"] = "Round", ["tags"] = new List<object?> { "a" } }, _inner);
        var twin = _diagram.AddNode("Circle", "c", null, _outer);
        _strong = _diagram.AddRelation("Strong", "s", _outer, _circle);
        _weak = _diagram.AddRelation("Weak", "w", twin, _circle);
    }

    private object? Evaluate(string expression, DislElement? self = null) =>
        Specification.Environment(DislContexts.Element).Compile(expression)
            .Evaluate(new Dictionary<string, object?> { ["self"] = self ?? _outer, ["diagram"] = _diagram, ["env"] = new CelMap() });

    [Theory]
    [InlineData("self.id", "outer")]
    [InlineData("self.type", "Box")]
    [InlineData("self.kind", "node")]
    [InlineData("self.name", "Outer")]
    [InlineData("self.label()", "Outer")]
    [InlineData("diagram.title", "Shapes")]
    [InlineData("diagram.unit", "month")]
    [InlineData("diagram.kind", "diagram")]
    public void TheMembers_ReadTheElement(string expression, object expected) => Assert.Equal(expected, Evaluate(expression));

    [Theory]
    [InlineData("self.size", 3L)]
    [InlineData("self.when", 24320L)]
    [InlineData("self.when.month()", 9L)]
    [InlineData("self.children[0].size", 7L)]
    [InlineData("self.children[0].when", 0L)]
    [InlineData("self.weight == 0.0 ? 1 : 0", 1L)]
    [InlineData("size(self.tags)", 0L)]
    [InlineData("size(self.children[0].children[0].tags)", 1L)]
    public void AnUnsetAttribute_ReadsAsItsDefaultOrItsZeroValue(string expression, long expected) => Assert.Equal(expected, Evaluate(expression));

    [Theory]
    [InlineData("has(self.name)", true)]
    [InlineData("has(self.size)", false)]
    [InlineData("has(self.children[0].size)", true)]
    [InlineData("has(diagram.title)", true)]
    [InlineData("has(diagram.unit)", false)]
    [InlineData("has(self.parent)", false)]
    [InlineData("has(self.children[0].parent)", true)]
    public void Has_HoldsForAStoredValueOnly(string expression, bool expected) => Assert.Equal(expected, Evaluate(expression));

    [Theory]
    [InlineData("self.isA('Box')", true)]
    [InlineData("self.isA('Shape')", true)]
    [InlineData("self.isA('Circle')", false)]
    [InlineData("diagram.relations[0].isA('Link')", true)]
    [InlineData("diagram.relations[0].isA('Weak')", false)]
    public void IsA_IncludesSubtypes(string expression, bool expected) => Assert.Equal(expected, Evaluate(expression));

    [Theory]
    [InlineData("self.parent == null", true)]
    [InlineData("self.children.map(c, c.id) == ['inner', 'c']", true)]
    [InlineData("self.descendants().map(c, c.id) == ['inner', 'c', 'c']", true)]
    [InlineData("self.descendants()[1].ancestors().map(c, c.id) == ['inner', 'outer']", true)]
    [InlineData("self.childrenOfType('Circle').size() == 1", true)]
    [InlineData("self.children[0].parent == self", true)]
    [InlineData("self.owner == diagram", true)]
    public void Containment_IsReadParentsFirst(string expression, bool expected) => Assert.Equal(expected, Evaluate(expression));

    [Theory]
    [InlineData("self.outgoing.map(r, r.id) == ['s']", true)]
    [InlineData("self.incoming.size() == 0", true)]
    [InlineData("self.descendants()[1].incoming.map(r, r.id) == ['s', 'w']", true)]
    [InlineData("self.descendants()[1].incomingOf('Link').size() == 2", true)]
    [InlineData("self.descendants()[1].incomingOf('Weak').map(r, r.source.id) == ['c']", true)]
    [InlineData("self.outgoingOf('Weak').size() == 0", true)]
    [InlineData("diagram.relations[0].source == self", true)]
    [InlineData("diagram.relations[0].target.name == 'Round'", true)]
    [InlineData("diagram.relations[0].other(self).name == 'Round'", true)]
    [InlineData("diagram.relations[0].kind == 'relation'", true)]
    public void Relations_AreReadFromTheirEnds(string expression, bool expected) => Assert.Equal(expected, Evaluate(expression));

    [Theory]
    [InlineData("self.descendants()[1].positionIn(self.descendants())", 1L)]
    [InlineData("self.descendants()[2].positionIn(self.descendants())", 2L)]
    [InlineData("self.positionIn(self.children)", -1L)]
    [InlineData("diagram.nodes.filter(n, n.id == 'c').size()", 2L)]
    public void PositionIn_ComparesByIdentityNotById(string expression, long expected) => Assert.Equal(expected, Evaluate(expression));

    [Theory]
    [InlineData("diagram.nodes.map(n, n.id) == ['outer', 'inner', 'c', 'c']", true)]
    [InlineData("diagram.nodesOfType('Shape').size() == 4", true)]
    [InlineData("diagram.nodesOfType('Circle').size() == 2", true)]
    [InlineData("diagram.nodesOfType('Circle', true).size() == 2", true)]
    [InlineData("diagram.relationsOfType('Link').map(r, r.id) == ['s', 'w']", true)]
    [InlineData("diagram.elementById('c').name == 'Round'", true)]
    [InlineData("diagram.elementById('none') == null", true)]
    [InlineData("diagram.elements.size() == 6", true)]
    public void TheDiagram_ListsItsElementsInModelOrder(string expression, bool expected) => Assert.Equal(expected, Evaluate(expression));

    [Fact]
    public void AReference_ReadsAsTheElementItNames() => Assert.Same(_circle, Evaluate("self.buddy"));

    [Fact]
    public void AnElement_IsNotItsTwin() => Assert.Equal(false, Evaluate("diagram.nodes[2] == diagram.nodes[3]"));

    [Fact]
    public void AMemberANodeLacks_IsAnError() =>
        Assert.Equal(new CelError("No such field: 'source'."), Evaluate("self.source"));

    [Fact]
    public void ARelationMethodOnANode_IsAnError() =>
        Assert.Equal(new CelError("'other()' is not a method of this value."), Evaluate("self.other(self)"));

    [Fact]
    public void AnAbstractTypeOrAnUndeclaredAttribute_CannotBeAdded()
    {
        Assert.Throws<ArgumentException>(() => _diagram.AddNode("Shape", "x"));
        Assert.Throws<ArgumentException>(() => _diagram.AddNode("Box", "x", new Dictionary<string, object?> { ["colour"] = "red" }));
        Assert.Throws<ArgumentException>(() => _diagram.AddRelation("Box", "x", _outer, _inner));
        Assert.Same(_weak, _diagram.ElementById("w"));
        Assert.Same(_strong, _diagram.Relations[0]);
    }
}
