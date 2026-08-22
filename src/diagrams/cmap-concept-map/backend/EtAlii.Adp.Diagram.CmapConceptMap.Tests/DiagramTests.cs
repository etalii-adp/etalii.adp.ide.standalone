using Xunit;

namespace EtAlii.Adp.Diagram.CmapConceptMap.Tests;

public class DiagramTests
{
    [Fact]
    public void Definition_Origin_MatchesTheCatalogedOriginTag()
    {
        var origin = EtAlii.Adp.Diagram.CmapConceptMap.Diagram.Definition.Origin;

        Assert.Equal("cmap", origin.Vendor);
        Assert.Equal("concept-map", origin.Type);
        Assert.Equal("", origin.Subtype);
    }

    [Fact]
    public void Definition_Title_MatchesTheCatalogedDiagramName()
    {
        Assert.Equal(
            "Concept map (free-form network of concepts with labeled relationships)",
            EtAlii.Adp.Diagram.CmapConceptMap.Diagram.Definition.Title);
    }
}
