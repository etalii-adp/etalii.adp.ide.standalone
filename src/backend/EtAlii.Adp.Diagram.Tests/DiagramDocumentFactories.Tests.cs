using Xunit;

namespace EtAlii.Adp.Diagram.Tests;

public class DiagramDocumentFactoriesTests
{
    private static readonly DiagramOrigin Mindmap = new("freeplane", "mindmap");
    private static readonly DiagramOrigin ClassDiagram = new("uml", "class");

    [Fact]
    public void Find_ReturnsTheFactoryRegisteredForAnOrigin()
    {
        var factory = new StubFactory(Mindmap);
        var factories = new DiagramDocumentFactories([factory]);

        Assert.Same(factory, factories.Find(Mindmap));
        Assert.Null(factories.Find(ClassDiagram));
    }

    [Fact]
    public void Verify_ReportsADefinitionWithAnExtensionButNoFactory()
    {
        var factories = new DiagramDocumentFactories([]);
        var orphan = new DiagramDefinition(Mindmap, "Mind map", ".mm");

        var missing = factories.Verify([orphan]);

        Assert.Same(orphan, Assert.Single(missing));
    }

    [Fact]
    public void Verify_IsSilentForADefinitionWithoutAnExtension()
    {
        // The 57 existing modules: no sibling, so no factory needed and nothing to report.
        var factories = new DiagramDocumentFactories([]);

        var missing = factories.Verify([new DiagramDefinition(ClassDiagram, "Class diagram")]);

        Assert.Empty(missing);
    }

    [Fact]
    public void Verify_NamesEveryOffender_NotJustTheFirst()
    {
        var factories = new DiagramDocumentFactories([]);
        var first = new DiagramDefinition(Mindmap, "Mind map", ".mm");
        var second = new DiagramDefinition(new DiagramOrigin("d2", "diagram"), "D2", ".d2");

        var missing = factories.Verify([first, second]);

        Assert.Equal(2, missing.Count);
    }

    private sealed class StubFactory(DiagramOrigin origin) : IDiagramDocumentFactory
    {
        public DiagramOrigin Origin { get; } = origin;

        public string CreateEmptyDocument(string baseName) => "";
    }
}
