using Xunit;

namespace EtAlii.Adp.Diagram.NetworkTopology.Tests;

public class DiagramTests
{
    [Fact]
    public void Definition_Origin_MatchesTheCatalogedOriginTag()
    {
        var origin = Diagram.Definition.Origin;

        Assert.Equal("network", origin.Vendor);
        Assert.Equal("topology", origin.Type);
        Assert.Equal("", origin.Subtype);
    }

    [Fact]
    public void Definition_Title_MatchesTheCatalogedDiagramName()
    {
        Assert.Equal(
            "Network topology diagram",
            Diagram.Definition.Title);
    }
}
