using System.Text;
using EtAlii.Adp.Diagram;
using EtAlii.Adp.Specification.Fbl.Documents;
using EtAlii.Adp.Specification.Json;
using Xunit;

namespace EtAlii.Adp.Hierarchy.Tests;

/// <summary>
/// A new document made from a binding's <c>template</c> (FBL 13): the factory any tool whose binding has one
/// registers in place of writing its own, so the template is stated once, in the binding
/// (dotnet-dependency-graph-disl-fbl library item L6). The binding here is a neutral one; core names no tool.
/// </summary>
public class TemplateDocumentFactoryTests
{
    private static readonly DiagramDefinition _definition =
        new(new ToolOrigin("test", "templated"), "Templated", "A test type.", Extension: ".tpl", SharedExtension: true, AlternateExtension: ".old");

    [Fact]
    public void TheNewDocument_IsTheTemplate_WithItsPlaceholdersReplacedForTheFileCoreWillWrite()
    {
        // Arrange.
        var factory = new TemplateDocumentFactory(_definition, Binding("""{ "text": "<Root file=\"{name}\" base=\"{base}\" key=\"{key}\" id=\"{newid:node}\" />\r\n" }"""), rule => $"made-{rule}");

        // Act.
        var text = factory.CreateEmptyDocument("New plan");

        // Assert: {name} is the base name plus the primary extension, the file core names the sibling.
        Assert.Equal("<Root file=\"New plan.tpl\" base=\"New plan\" key=\"New_plan\" id=\"made-node\" />\r\n", text);
        Assert.Equal(_definition.Origin, factory.Origin);
    }

    [Fact]
    public void TheNewDocument_IsTheTemplateOfTheTypesOrigin_WhereTheBindingHasOne()
    {
        // Arrange.
        var factory = new TemplateDocumentFactory(_definition, Binding("""{ "text": "general", "byOrigin": { "test/templated": "for {base}", "test/other": "other" } }"""));

        // Act.
        var text = factory.CreateEmptyDocument("Plan");

        // Assert.
        Assert.Equal("for Plan", text);
    }

    [Fact]
    public void ABindingWithoutATemplate_IsRefused_RatherThanMakingAnEmptyDocument()
    {
        // Arrange.
        var binding = Binding(template: null);

        // Act.
        var error = Assert.Throws<ArgumentException>(() => new TemplateDocumentFactory(_definition, binding));

        // Assert.
        Assert.Contains("template", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ATypeThatKeepsNoSibling_IsRefused()
    {
        // Arrange: a registration-only type has no document for a template to make.
        var bare = new DiagramDefinition(new ToolOrigin("test", "bare"), "Bare");

        // Act.
        var error = Assert.Throws<ArgumentException>(() => new TemplateDocumentFactory(bare, Binding("""{ "text": "x" }""")));

        // Assert.
        Assert.Contains("extension", error.Message, StringComparison.Ordinal);
    }

    /// <summary>A neutral binding with the given <c>template</c> object, or none.</summary>
    private static FblBinding Binding(string? template)
    {
        var json = $$"""
            {
              "fbl": "0.2",
              "bindings": {
                "templated": {
                  "claims": { "extensions": [".tpl", ".old"], "origins": ["test/templated"], "shared": true, "registrationOnly": true },
                  "body": { "kind": "file" },
                  "reader": { "plugin": "net.etalii.adp.test.templated", "version": "^0.1.0" },
                  "readOnly": true{{(template is null ? "" : $",\n      \"template\": {template}")}}
                }
              }
            }
            """;
        var problems = FblDocumentLoader.Load(Encoding.UTF8.GetBytes(json), out var document);
        Assert.DoesNotContain(problems, problem => problem.Severity == LoadSeverity.Error);
        return document!.Bindings["templated"];
    }
}
