using Xunit;

namespace EtAlii.Adp.Diagram.Tests;

public class DiagramDocumentFactoriesTests
{
    private static readonly DiagramOrigin Mindmap = new("freeplane", "mindmap");
    private static readonly DiagramOrigin ClassDiagram = new("uml", "class");

    [Fact]
    public void Find_ReturnsTheFactoryRegisteredForAnOrigin()
    {
        // Arrange and act.
        var factory = new DiagramDocumentFactoriesStubFactory(Mindmap);
        var factories = new DiagramDocumentFactories([factory]);

        // Assert.
        Assert.Same(factory, factories.Find(Mindmap));
        Assert.Null(factories.Find(ClassDiagram));
    }

    [Fact]
    public void Verify_ReportsADefinitionWithAnExtensionButNoFactory()
    {
        // Arrange.
        var factories = new DiagramDocumentFactories([]);
        var orphan = new DiagramDefinition(Mindmap, "Mind map", Extension: ".mm");

        // Act.
        var missing = factories.Verify([orphan]);

        // Assert.
        Assert.Same(orphan, Assert.Single(missing));
    }

    [Fact]
    public void Verify_IsSilentForADefinitionWithoutAnExtension()
    {
        // Arrange.
        // The 57 existing modules: no sibling, so no factory needed and nothing to report.
        var factories = new DiagramDocumentFactories([]);

        // Act.
        var missing = factories.Verify([new DiagramDefinition(ClassDiagram, "Class diagram")]);

        // Assert.
        Assert.Empty(missing);
    }

    [Fact]
    public void Verify_NamesEveryOffender_NotJustTheFirst()
    {
        // Arrange.
        var factories = new DiagramDocumentFactories([]);
        var first = new DiagramDefinition(Mindmap, "Mind map", Extension: ".mm");
        var second = new DiagramDefinition(new DiagramOrigin("d2", "diagram"), "D2", Extension: ".d2");

        // Act.
        var missing = factories.Verify([first, second]);

        // Assert.
        Assert.Equal(2, missing.Count);
    }

}
