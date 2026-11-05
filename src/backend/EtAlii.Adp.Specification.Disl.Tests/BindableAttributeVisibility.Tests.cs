using Xunit;

namespace EtAlii.Adp.Specification.Disl.Tests;

/// <summary>
/// A Bindable bound to an attribute (<c>{"attribute": name}</c>, DISL §6, appendix) names the attribute; it is not CEL,
/// also where the property it binds is an Expression elsewhere, as a badge's <c>visible</c>.
/// </summary>
public class BindableAttributeVisibilityTests
{
    [Fact]
    public void ABadgeVisibleBoundToAnAttribute_Loads()
    {
        var result = DislLoader.Load(Specifications.With("""
            "metamodel": { "types": { "Thing": { "attributes": { "name": { "type": "string" }, "heavy": { "type": "bool" } } } } },
            "notation": { "nodes": { "Thing": { "shape": "rect", "badges": [ { "id": "weight", "text": "!", "visible": { "attribute": "heavy" } } ] } } }
            """));

        Assert.Empty(result.Errors);
    }

    [Fact]
    public void ABadgeVisibleAsCel_StillCompiles()
    {
        var result = DislLoader.Load(Specifications.With("""
            "metamodel": { "types": { "Thing": { "attributes": { "name": { "type": "string" }, "heavy": { "type": "bool" } } } } },
            "notation": { "nodes": { "Thing": { "shape": "rect", "badges": [ { "id": "weight", "text": "!", "visible": { "cel": "heavy" } } ] } } }
            """));

        Assert.Contains(result.Errors, error => error.ToString().Contains("/notation/nodes/Thing/badges/0/visible/cel", StringComparison.Ordinal));
    }
}
