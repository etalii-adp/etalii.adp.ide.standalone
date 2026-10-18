using EtAlii.Adp.Specification.Cel;
using EtAlii.Adp.Specification.Fbl;
using Xunit;

namespace EtAlii.Adp.Specification.Disl.Tests;

/// <summary>
/// A diagram attribute read as an element's is (DISL §4.3, §12.2): a derived one is its expression,
/// with <c>self</c> the diagram and <c>env</c> the diagram's, and a reference resolves to its element.
/// </summary>
public class DiagramAttributeValuesTests
{
    private static readonly DislSpecification Specification = Specifications.Loaded(Specifications.With("""
        "metamodel": {
          "diagram": { "attributes": {
            "title": { "type": "string", "default": "" },
            "heading": { "type": "string", "derived": "self.title != '' ? self.title : 'A ' + env.viewpoint + ' of ' + string(self.nodes.size())" } } },
          "types": { "Box": { "attributes": { "name": { "type": "string" } } } } },
        "viewpoints": { "plan": {} }
        """));

    private static object? Evaluate(DislDiagram diagram, string expression) =>
        Specification.Environment(DislContexts.Element).Compile(expression)
            .Evaluate(new Dictionary<string, object?> { ["self"] = diagram, ["diagram"] = diagram, ["env"] = new CelMap() });

    [Fact]
    public void ADerivedDiagramAttribute_IsItsExpression()
    {
        // Arrange.
        var diagram = new DislDiagram(Specification) { Env = new DislEnv(Viewpoint: "plan") };
        diagram.AddNode("Box", "a");
        var titled = new DislDiagram(Specification, new Dictionary<string, object?> { ["title"] = "Mine" });

        // Act.
        var heading = diagram.ValueOf("heading");
        var read = Evaluate(diagram, "diagram.heading");
        var has = Evaluate(diagram, "has(diagram.heading)");
        var own = titled.ValueOf("heading");

        // Assert.
        Assert.Equal("A plan of 1", heading);
        Assert.Equal("A plan of 1", read);
        Assert.Equal(true, has);
        Assert.Equal("Mine", own);
    }

    private static readonly DislSpecification Referring = Specifications.Loaded(Specifications.With("""
        "metamodel": {
          "diagram": { "attributes": { "focus": { "type": "Box" }, "also": { "type": "Box", "many": true } } },
          "types": { "Box": { "attributes": { "name": { "type": "string" } } } } },
        "persistence": { "typeMap": { "Setting": { "as": "diagram" } } }
        """));

    [Fact]
    public void AReferenceDiagramAttribute_ResolvesToItsElement()
    {
        // Arrange.
        var reading = new FblModel(
        [
            new FblElement("setting@1", false, "Setting", "setting", false, new Dictionary<string, object?> { ["focus"] = "b", ["also"] = new List<object?> { "a", "nowhere" } }, null, null, null, null, new Span(0, 0), 1),
            new FblElement("a", true, "Box", "box", false, new Dictionary<string, object?> { ["name"] = "A" }, null, null, null, null, new Span(0, 0), 2),
            new FblElement("b", true, "Box", "box", false, new Dictionary<string, object?> { ["name"] = "B" }, null, null, null, null, new Span(0, 0), 3),
        ], [], false);
        var diagram = DislModelBuilder.From(reading, Referring).Diagram;

        // Act.
        var focus = diagram.ValueOf("focus");
        var also = Assert.IsAssignableFrom<IReadOnlyList<object?>>(diagram.ValueOf("also"));
        var name = Referring.Environment(DislContexts.Element).Compile("diagram.focus.name")
            .Evaluate(new Dictionary<string, object?> { ["self"] = diagram, ["diagram"] = diagram, ["env"] = new CelMap() });

        // Assert.
        Assert.Same(diagram.ElementById("b"), focus);
        Assert.Equal([diagram.ElementById("a"), null], also);
        Assert.Equal("B", name);
    }
}
