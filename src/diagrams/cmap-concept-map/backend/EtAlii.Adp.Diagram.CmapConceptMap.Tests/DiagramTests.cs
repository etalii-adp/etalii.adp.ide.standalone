using Xunit;

namespace EtAlii.Adp.Diagram.CmapConceptMap.Tests;

public class DiagramTests
{
    [Fact]
    public void Definition_Origin_MatchesTheCatalogedOriginTag()
    {
        // Act.
        var origin = Diagram.Definition.Origin;

        // Assert.
        Assert.Equal("cmap", origin.Vendor);
        Assert.Equal("concept-map", origin.Type);
        Assert.Equal("", origin.Subtype);
    }

    [Fact]
    public void Definition_Title_MatchesTheCatalogedDiagramName()
    {
        // Arrange, act and assert.
        Assert.Equal(
            "Concept map (free-form network of concepts with labeled relationships)",
            Diagram.Definition.Title);
    }
}
