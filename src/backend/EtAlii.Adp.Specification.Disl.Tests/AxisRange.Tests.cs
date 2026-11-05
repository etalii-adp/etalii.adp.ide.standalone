using EtAlii.Adp.Specification.Cel;
using Xunit;

namespace EtAlii.Adp.Specification.Disl.Tests;

/// <summary>
/// <c>axisRange(axis, value)</c> (DISL §12.4, §5.3): the id of the range of an axis that holds a value, <c>from</c>
/// included and <c>to</c> excluded except at the axis maximum, and <c>''</c> when none does.
/// </summary>
public class AxisRangeTests
{
    private static readonly DislSpecification Specification = Specifications.Loaded(Specifications.With("""
        "coordinates": { "axes": {
          "across": { "kind": "linear", "min": 0, "max": 1, "ranges": [
            { "id": "low", "from": 0, "to": 0.25 },
            { "id": "middle", "from": 0.25, "to": 0.6 },
            { "id": "high", "from": 0.75, "to": 1 } ] },
          "plain": { "kind": "linear", "min": 0, "max": 10 } } }
        """));

    private static object? Evaluate(string expression) =>
        Specification.Environment(DislContext.Element).Compile(expression).Evaluate(new Dictionary<string, object?>());

    [Theory]
    [InlineData("axisRange('across', 0)", "low")]
    [InlineData("axisRange('across', 0.1)", "low")]
    [InlineData("axisRange('across', 0.25)", "middle")]
    [InlineData("axisRange('across', 0.5)", "middle")]
    [InlineData("axisRange('across', 0.75)", "high")]
    [InlineData("axisRange('across', 1)", "high")]
    [InlineData("axisRange('across', 1.0)", "high")]
    public void TheRangeHoldingAValue_IsNamedByItsId(string expression, string expected) =>
        Assert.Equal(expected, Evaluate(expression));

    [Theory]
    [InlineData("axisRange('across', 0.7)")]
    [InlineData("axisRange('across', -0.1)")]
    [InlineData("axisRange('across', 1.5)")]
    [InlineData("axisRange('plain', 3)")]
    public void AValueNoRangeHolds_IsTheEmptyString(string expression) =>
        Assert.Equal("", Evaluate(expression));

    [Fact]
    public void AnAxisTheSpecificationDoesNotDeclare_IsAnError() =>
        Assert.Equal(new CelError("'nowhere' is not an axis of this specification (§5.3)."), Evaluate("axisRange('nowhere', 0.5)"));

    [Fact]
    public void ACallFromADerivedAttribute_Loads()
    {
        var result = DislLoader.Load(Specifications.With("""
            "coordinates": { "axes": { "across": { "kind": "linear", "min": 0, "max": 1, "ranges": [ { "id": "low", "from": 0, "to": 1 } ] } } },
            "metamodel": { "types": { "Thing": { "attributes": {
              "x": { "type": "number" },
              "band": { "type": "string", "derived": "axisRange('across', self.x)" } } } } }
            """));
        Assert.Empty(result.Errors);
    }
}
