using EtAlii.Adp.Specification.Cel;
using Xunit;

namespace EtAlii.Adp.Specification.Disl.Tests;

/// <summary>
/// <c>b.center()</c> on a Bounds (DISL §12.2): the point halfway across and down, as
/// <c>[x, y]</c> doubles, for the bounds a host binds in an element's <c>view</c>.
/// </summary>
public class BoundsCenterTests
{
    private static readonly DislSpecification Specification = Specifications.Loaded(Specifications.With("""
        "metamodel": { "types": { "Spot": {}, "Ring": { "attributes": { "around": { "type": "Spot", "many": true } } } } }
        """));

    private static object? Evaluate(string expression, DislElement self, DislDiagram diagram) =>
        Specification.Environment(DislContext.Element).Compile(expression)
            .Evaluate(new Dictionary<string, object?> { ["self"] = self, ["diagram"] = diagram, ["env"] = new CelMap() });

    private static CelMap Bounds(object x, object y, object width, object height) =>
        new() { ["bounds"] = new CelMap { ["x"] = x, ["y"] = y, ["width"] = width, ["height"] = height } };

    [Fact]
    public void TheCenter_IsHalfwayAcrossAndDown()
    {
        // Arrange.
        var diagram = new DislDiagram(Specification);
        var spot = diagram.AddNode("Spot", "s");
        spot.View = Bounds(10L, 20L, 100L, 40L);

        // Act.
        var center = Evaluate("self.view.bounds.center()", spot, diagram);

        // Assert.
        Assert.Equal(new List<object?> { 60.0, 40.0 }, center);
    }

    [Fact]
    public void TheCentersOfSeveralElements_Average()
    {
        // Arrange.
        var diagram = new DislDiagram(Specification);
        var left = diagram.AddNode("Spot", "l");
        var right = diagram.AddNode("Spot", "r");
        left.View = Bounds(0.0, 0.0, 20.0, 10.0);
        right.View = Bounds(100.0, 50.0, 20.0, 10.0);
        var ring = diagram.AddNode("Ring", "ring", new Dictionary<string, object?> { ["around"] = new List<object?> { "l", "r" } });

        // Act.
        var x = Evaluate("self.around.map(m, m.view.bounds.center()[0]).avg()", ring, diagram);
        var y = Evaluate("self.around.map(m, m.view.bounds.center()[1]).avg()", ring, diagram);

        // Assert.
        Assert.Equal((60.0, 30.0), (x, y));
    }

    [Fact]
    public void BoundsWithoutASize_HaveNoCenter()
    {
        // Arrange.
        var diagram = new DislDiagram(Specification);
        var spot = diagram.AddNode("Spot", "s");
        spot.View = new CelMap { ["bounds"] = new CelMap { ["x"] = 1L, ["y"] = 2L } };

        // Act.
        var center = Evaluate("self.view.bounds.center()", spot, diagram);

        // Assert.
        Assert.Equal(new CelError("center() needs bounds with x, y, width and height."), center);
    }
}
