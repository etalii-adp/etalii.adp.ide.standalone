using Xunit;

namespace EtAlii.Adp.Diagram.ArchimateStrategy.Tests;

public class DiagramTests
{
    [Fact]
    public void Definition_Origin_MatchesTheCatalogedOriginTag()
    {
        var origin = EtAlii.Adp.Diagram.ArchimateStrategy.Diagram.Definition.Origin;

        Assert.Equal("archimate", origin.Vendor);
        Assert.Equal("strategy", origin.Type);
        Assert.Equal("", origin.Subtype);
    }

    [Fact]
    public void Definition_Title_MatchesTheCatalogedDiagramName()
    {
        Assert.Equal(
            "ArchiMate — Strategy layer",
            EtAlii.Adp.Diagram.ArchimateStrategy.Diagram.Definition.Title);
    }
}
