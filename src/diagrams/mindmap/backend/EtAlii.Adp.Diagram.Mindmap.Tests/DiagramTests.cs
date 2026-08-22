using Xunit;

namespace EtAlii.Adp.Diagram.Mindmap.Tests;

public class DiagramTests
{
    [Fact]
    public void Definition_Origin_MatchesTheCatalogedOriginTag()
    {
        var origin = EtAlii.Adp.Diagram.Mindmap.Diagram.Definition.Origin;

        Assert.Equal("freeplane", origin.Vendor);
        Assert.Equal("mindmap", origin.Type);
        Assert.Equal("", origin.Subtype);
    }

    [Fact]
    public void Definition_Title_MatchesTheCatalogedDiagramName()
    {
        Assert.Equal(
            "Mind map (radial/hierarchical, single central topic)",
            EtAlii.Adp.Diagram.Mindmap.Diagram.Definition.Title);
    }
}
