using System.Text;
using EtAlii.Adp.Specification.Fbl.Documents;
using Xunit;

namespace EtAlii.Adp.Specification.Disl.Tests;

/// <summary>
/// The attribute types a specification gives a binding's types (fbl-written-text Requirement 8),
/// resolved through <c>persistence.typeMap</c>: a mapped type, a renamed attribute, an inherited one
/// and a host attribute, and the types that give none.
/// </summary>
public class DislAttributeTypesTests
{
    private static readonly DislSpecification Specification = Specifications.Loaded(Specifications.With("""
        "metamodel": {
          "diagram": { "attributes": { "scale": { "type": "int" } } },
          "types": {
            "Base": { "abstract": true, "attributes": { "title": { "type": "string" }, "start": { "type": "datetime" }, "lane": { "type": "int" } } },
            "Span": { "extends": "Base", "attributes": { "until": { "type": "date" }, "done": { "type": "bool" } } },
            "Point": { "extends": "Base" } } },
        "persistence": { "typeMap": {
          "Bar": { "as": "Span", "attributes": { "caption": "title" }, "hostAttributes": ["raw"] },
          "Top": { "as": "header" },
          "Settings": { "as": "diagram", "attributes": { "zoom": "scale" } },
          "Junk": { "as": "unreadable" } } }
        """));

    private static readonly Func<string, string, string?> TypeOf = DislAttributeTypes.Of(Specification);

    [Theory]
    [InlineData("Bar", "done", "bool")]
    [InlineData("Bar", "until", "date")]
    [InlineData("Bar", "caption", "string")]
    [InlineData("Bar", "start", "datetime")]
    [InlineData("Bar", "lane", "int")]
    [InlineData("Bar", "raw", "string")]
    [InlineData("Point", "lane", "int")]
    [InlineData("Settings", "zoom", "int")]
    public void ABindingAttributeHasTheTypeOfTheAttributeItMapsTo(string type, string attribute, string expected)
    {
        // Act.
        var observed = TypeOf(type, attribute);

        // Assert: mapped, renamed, inherited through extends, a host attribute, a type the typeMap does not name, the diagram.
        Assert.Equal(expected, observed);
    }

    [Theory]
    [InlineData("Top", "title")]
    [InlineData("Junk", "title")]
    [InlineData("Bar", "nothing")]
    [InlineData("Nowhere", "title")]
    public void AnAttributeTheSpecificationDoesNotTypeHasNoType(string type, string attribute)
    {
        // Act.
        var observed = TypeOf(type, attribute);

        // Assert: a header, an unreadable type, an unknown attribute and an unknown type.
        Assert.Null(observed);
    }

    [Fact]
    public void TheTimeAttributesAreTheBindingsDateAndDateTimeAttributesByRule()
    {
        // Arrange.
        var json = """
            { "fbl": "0.5", "bindings": { "t": { "claims": { "extensions": [".t"] }, "body": { "kind": "file", "family": "yaml" }, "reader": "declared",
              "elements": [
                { "name": "bar", "type": "Bar", "at": "/bars/*", "attributes": { "caption": { "key": "caption" }, "start": { "key": "start" }, "until": { "key": "until" }, "lane": { "key": "lane" } } },
                { "name": "point", "type": "Point", "at": "/points/*", "attributes": { "start": { "key": "at" } } } ] } } }
            """;
        Assert.Empty(FblDocumentLoader.Load(Encoding.UTF8.GetBytes(json), out var document));

        // Act.
        var observed = DislAttributeTypes.TimeAttributes(Specification, document!.Bindings["t"]);

        // Assert.
        Assert.Equal(["bar.start", "bar.until", "point.start"], observed.Order(StringComparer.Ordinal));
    }
}
