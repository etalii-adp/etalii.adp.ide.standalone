using Xunit;

namespace EtAlii.Adp.Specification.Disl.Tests;

/// <summary>
/// A number a specification writes as a literal reaches CEL as a value CEL reads from a body would be: a whole
/// number as an int and any other number as a double (behaviour change B-38, ruled 2026-10-20), so that the int
/// operators take a whole literal.
/// </summary>
public class LiteralNumbersTests
{
    private static readonly DislSpecification Specification = Specifications.Loaded(Specifications.With("""
        "metamodel": {
          "types": {
            "Crate": { "attributes": {
              "whole": { "type": "json", "default": 7 },
              "fraction": { "type": "json", "default": 7.5 } } } } }
        """));

    private readonly DislDiagram _diagram = new(Specification);

    private object? Evaluate(string expression, DislElement self) =>
        Specification.Environment(DislContext.Element).Compile(expression)
            .Evaluate(new Dictionary<string, object?> { ["self"] = self, ["diagram"] = _diagram, ["env"] = new DislEnv().ToCel() });

    [Fact]
    public void AWholeLiteral_IsAnInt_SoModuloTakesIt()
    {
        // Arrange.
        var crate = _diagram.AddNode("Crate", "crate", new Dictionary<string, object?>());

        // Act.
        var remainder = Evaluate("self.whole % 2", crate);

        // Assert.
        Assert.Equal(1L, remainder);
    }

    [Fact]
    public void AWholeLiteral_DividesWhole_AndAnyOtherNumberIsADouble()
    {
        // Arrange.
        var crate = _diagram.AddNode("Crate", "crate", new Dictionary<string, object?>());

        // Act.
        var whole = Evaluate("self.whole / 2", crate);
        var fraction = Evaluate("self.fraction / 2", crate);

        // Assert.
        Assert.Equal(3L, whole);
        Assert.Equal(3.75, fraction);
    }
}
