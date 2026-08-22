using Xunit;

namespace EtAlii.Adp.Diagram.WardleyMap.Tests;

public class DiagramTests
{
    [Fact]
    public void Definition_Origin_MatchesTheCatalogedOriginTag()
    {
        var origin = Diagram.Definition.Origin;

        Assert.Equal("wardley", origin.Vendor);
        Assert.Equal("map", origin.Type);
        Assert.Equal("", origin.Subtype);
    }

    [Fact]
    public void Definition_Title_MatchesTheCatalogedDiagramName()
    {
        Assert.Equal(
            "Wardley Map",
            Diagram.Definition.Title);
    }
}
