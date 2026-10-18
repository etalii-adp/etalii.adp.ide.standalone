using EtAlii.Adp.Specification.Cel;
using Xunit;

namespace EtAlii.Adp.Specification.Disl.Tests;

/// <summary>
/// A diagram attribute read as an element's is (DISL §4.3, §12.2): a derived one is its expression,
/// with <c>self</c> the diagram and <c>env</c> the diagram's.
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
}
