using Xunit;

namespace EtAlii.Adp.Diagram.TogafAdm.Tests;

public class DiagramTests
{
    [Fact]
    public void Definition_Origin_MatchesTheCatalogedOriginTag()
    {
        var origin = Diagram.Definition.Origin;

        Assert.Equal("togaf", origin.Vendor);
        Assert.Equal("adm", origin.Type);
        Assert.Equal("", origin.Subtype);
    }

    [Fact]
    public void Definition_Title_MatchesTheCatalogedDiagramName()
    {
        Assert.Equal(
            "TOGAF ADM cycle diagram",
            Diagram.Definition.Title);
    }
}
