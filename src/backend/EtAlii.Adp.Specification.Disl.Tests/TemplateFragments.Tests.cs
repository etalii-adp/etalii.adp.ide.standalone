using Xunit;

namespace EtAlii.Adp.Specification.Disl.Tests;

/// <summary>
/// A toolbox template's fragment (DISL §7.4): its attribute values are Expressions, strings
/// included - <c>members</c> too, an Expression wherever it occurs - compiled in the <c>template</c> context (§12.3), which binds <c>p</c>, <c>diagram</c>,
/// <c>env</c> and <c>refs</c>, the elements the fragment creates by their <c>ref</c>.
/// </summary>
public class TemplateFragmentsTests
{
    private static string Template(string attributes) => Specifications.With($$"""
        "metamodel": {
          "types": { "Spot": { "attributes": { "name": { "type": "string" } } },
                     "Ring": { "attributes": { "title": { "type": "string" }, "members": { "type": "Spot", "many": true } } } },
          "relations": { "Path": { "source": "Spot", "target": "Spot", "attributes": { "note": { "type": "string" } } } } },
        "toolbox": { "templates": { "pair": {
          "label": "Pair",
          "params": { "first": { "type": "string", "default": "one" } },
          "fragment": {
            "nodes": [
              { "ref": "one", "type": "Spot", "attributes": { "name": "p.first" }, "at": [0, 0] },
              { "ref": "two", "type": "Spot", "attributes": { "name": "'two'" }, "at": [100, 0] },
              { "ref": "ring", "type": "Ring", "attributes": {{attributes}} }
            ],
            "relations": [ { "type": "Path", "source": "one", "target": "two", "attributes": { "note": "'from ' + p.first" } } ] } } } }
        """);

    [Fact]
    public void AFragmentReadingItsRefsAndParameters_Loads()
    {
        // Arrange.
        var json = Template("""{ "title": "'Ring of ' + p.first", "members": "[refs.one, refs.two]" }""");

        // Act.
        var diagnostics = Specifications.Diagnostics(json);

        // Assert.
        Assert.Empty(diagnostics);
    }

    [Theory]
    [InlineData("/toolbox/templates/pair/fragment/nodes/0/attributes/name")]
    [InlineData("/toolbox/templates/pair/fragment/nodes/1/attributes/name")]
    [InlineData("/toolbox/templates/pair/fragment/nodes/2/attributes/title")]
    [InlineData("/toolbox/templates/pair/fragment/nodes/2/attributes/members")]
    [InlineData("/toolbox/templates/pair/fragment/relations/0/attributes/note")]
    public void EveryAttributeValueOfAFragment_IsCompiledInTheTemplateContext(string pointer)
    {
        // Arrange.
        var specification = Specifications.Loaded(Template("""{ "title": "'Ring of ' + p.first", "members": "[refs.one, refs.two]" }"""));

        // Act.
        var expression = specification.ExpressionAt(pointer);

        // Assert.
        Assert.Equal(DislContext.Template, expression?.Context);
    }

    [Fact]
    public void AFragmentReadingSelf_IsRefused()
    {
        // Arrange: a fragment is inserted, not drawn, so there is no self.
        var json = Template("""{ "title": "self.name" }""");

        // Act.
        var diagnostics = Specifications.Diagnostics(json);

        // Assert.
        Assert.Equal(["error at /toolbox/templates/pair/fragment/nodes/2/attributes/title: 'self' is not a variable here; available: p, diagram, env, refs. (context template)"], diagnostics);
    }
}
