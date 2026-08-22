using Xunit;

namespace EtAlii.Adp.Diagram.WardleyMap.Tests;

public class DiagramTests
{
    [Fact]
    public void Definition_Origin_MatchesTheCatalogedOriginTag()
    {
        var origin = EtAlii.Adp.Diagram.WardleyMap.Diagram.Definition.Origin;

        Assert.Equal("wardley", origin.Vendor);
        Assert.Equal("map", origin.Type);
        Assert.Equal("", origin.Subtype);
    }

    [Fact]
    public void Definition_Title_MatchesTheCatalogedDiagramName()
    {
        Assert.Equal(
            "Wardley Map",
            EtAlii.Adp.Diagram.WardleyMap.Diagram.Definition.Title);
    }
}
