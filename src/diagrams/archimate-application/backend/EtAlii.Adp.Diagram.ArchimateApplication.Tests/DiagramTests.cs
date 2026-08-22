using Xunit;

namespace EtAlii.Adp.Diagram.ArchimateApplication.Tests;

public class DiagramTests
{
    [Fact]
    public void Definition_Origin_MatchesTheCatalogedOriginTag()
    {
        var origin = Diagram.Definition.Origin;

        Assert.Equal("archimate", origin.Vendor);
        Assert.Equal("application", origin.Type);
        Assert.Equal("", origin.Subtype);
    }

    [Fact]
    public void Definition_Title_MatchesTheCatalogedDiagramName()
    {
        Assert.Equal(
            "ArchiMate — Application layer",
            Diagram.Definition.Title);
    }
}
