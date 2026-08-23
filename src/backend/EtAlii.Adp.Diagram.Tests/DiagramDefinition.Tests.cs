using Xunit;

namespace EtAlii.Adp.Diagram.Tests;

public class DiagramDefinitionTests
{
    [Fact]
    public void Constructor_KeepsTheOriginAndTitleItWasGiven()
    {
        var origin = new DiagramOrigin("uml", "class");

        var definition = new DiagramDefinition(origin, "Class diagram");

        Assert.Same(origin, definition.Origin);
        Assert.Equal("Class diagram", definition.Title);
    }

    [Fact]
    public void Constructor_WithoutAnExtension_TheAdpFileIsTheWholeDiagram()
    {
        // The default every existing module relies on: nothing about them changes.
        var definition = new DiagramDefinition(new DiagramOrigin("uml", "class"), "Class diagram");

        Assert.Equal("", definition.Extension);
        Assert.False(definition.HasDocumentSibling);
    }

    [Fact]
    public void Constructor_WithAnExtension_DeclaresADocumentSibling()
    {
        var definition = new DiagramDefinition(new DiagramOrigin("freeplane", "mindmap"), "Mind map", ".mm");

        Assert.Equal(".mm", definition.Extension);
        Assert.True(definition.HasDocumentSibling);
    }
}
