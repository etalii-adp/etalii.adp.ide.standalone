using EtAlii.Adp.Common;
using Xunit;

namespace EtAlii.Adp.Diagram.Tests;

public class DiagramDefinitionTests
{
    [Fact]
    public void Constructor_KeepsTheOriginAndTitleItWasGiven()
    {
        // Arrange.
        var origin = new DiagramOrigin("uml", "class");

        // Act.
        var definition = new DiagramDefinition(origin, "Class diagram");

        // Assert.
        Assert.Same(origin, definition.Origin);
        Assert.Equal("Class diagram", definition.Title);
    }

    [Fact]
    public void Constructor_WithoutAnExtension_TheAdpFileIsTheWholeDiagram()
    {
        // Act.
        // The default every existing module relies on: nothing about them changes.
        var definition = new DiagramDefinition(new DiagramOrigin("uml", "class"), "Class diagram");

        // Assert.
        Assert.Equal("", definition.Extension);
        Assert.False(definition.HasDocumentSibling);
    }

    [Fact]
    public void Constructor_WithAnExtension_DeclaresADocumentSibling()
    {
        // Act.
        var definition = new DiagramDefinition(new DiagramOrigin("freeplane", "mindmap"), "Mind map", Extension: ".mm");

        // Assert.
        Assert.Equal(".mm", definition.Extension);
        Assert.True(definition.HasDocumentSibling);
    }

    [Fact]
    public void Constructor_WithoutASubject_TheDiagramIsItsDocument()
    {
        // Act.
        // The default every existing module relies on: nothing about them changes.
        var definition = new DiagramDefinition(new DiagramOrigin("uml", "class"), "Class diagram");

        // Assert.
        Assert.Equal(DiagramSubject.Document, definition.Subject);
        Assert.False(definition.HasFolderSubject);
    }

    [Fact]
    public void Constructor_WithAFolderSubject_TheDiagramIsTheFolderItsRegistrationSitsIn()
    {
        // Act.
        var definition = new DiagramDefinition(
            new DiagramOrigin("ansible", "structure"),
            "Ansible project structure",
            Subject: DiagramSubject.Folder);

        // Assert.
        Assert.True(definition.HasFolderSubject);
        // A folder subject and a sibling body are mutually exclusive by construction: the
        // registration is the whole document, which is what makes the folder the subject.
        Assert.False(definition.HasDocumentSibling);
    }
}
