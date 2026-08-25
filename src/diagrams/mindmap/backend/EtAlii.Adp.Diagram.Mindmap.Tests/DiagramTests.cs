using Xunit;

namespace EtAlii.Adp.Diagram.Mindmap.Tests;

public class DiagramTests
{
    [Fact]
    public void Definition_Origin_MatchesTheCatalogedOriginTag()
    {
        // Act.
        var origin = Diagram.Definition.Origin;

        // Assert.
        Assert.Equal("freeplane", origin.Vendor);
        Assert.Equal("mindmap", origin.Type);
        Assert.Equal("", origin.Subtype);
    }

    [Fact]
    public void Definition_Title_MatchesTheCatalogedDiagramName()
    {
        // Arrange, act and assert.
        Assert.Equal(
            "Mind map (radial/hierarchical, single central topic)",
            Diagram.Definition.Title);
    }
}
