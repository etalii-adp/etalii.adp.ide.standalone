using Xunit;

namespace EtAlii.Adp.Diagram.C4SystemLandscape.Tests;

public class DiagramTests
{
    [Fact]
    public void Definition_Origin_MatchesTheCatalogedOriginTag()
    {
        var origin = Diagram.Definition.Origin;

        Assert.Equal("c4", origin.Vendor);
        Assert.Equal("system-landscape", origin.Type);
        Assert.Equal("", origin.Subtype);
    }

    [Fact]
    public void Definition_Title_MatchesTheCatalogedDiagramName()
    {
        Assert.Equal(
            "System Landscape (supplementary)",
            Diagram.Definition.Title);
    }
}
