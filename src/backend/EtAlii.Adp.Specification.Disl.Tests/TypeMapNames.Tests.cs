using Xunit;

namespace EtAlii.Adp.Specification.Disl.Tests;

/// <summary>
/// A type map (DISL §11.2) holds names, never CEL: a binding attribute that happens to be called
/// like a property that is an Expression elsewhere (<c>members</c>, <c>when</c>, <c>rule</c>) is still
/// the name of an attribute.
/// </summary>
public class TypeMapNamesTests
{
    [Fact]
    public void ABindingAttributeNamedLikeAnExpressionProperty_IsAName()
    {
        // Arrange.
        var json = Specifications.With("""
            "metamodel": {
              "diagram": { "attributes": { "includes": { "type": "string", "many": true }, "condition": { "type": "string" } } },
              "types": { "Thing": { "attributes": { "name": { "type": "string" } } } } },
            "persistence": { "format": "fbl", "binding": "things.fbl#things", "typeMap": {
              "Selection": { "as": "diagram", "attributes": { "members": "includes", "when": "condition", "rule": null } } } }
            """);

        // Act.
        var diagnostics = Specifications.Diagnostics(json);

        // Assert.
        Assert.Empty(diagnostics);
    }
}
