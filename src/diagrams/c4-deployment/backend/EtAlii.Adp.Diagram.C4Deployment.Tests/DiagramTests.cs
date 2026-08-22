using Xunit;

namespace EtAlii.Adp.Diagram.C4Deployment.Tests;

public class DiagramTests
{
    [Fact]
    public void Definition_Origin_MatchesTheCatalogedOriginTag()
    {
        var origin = Diagram.Definition.Origin;

        Assert.Equal("c4", origin.Vendor);
        Assert.Equal("deployment", origin.Type);
        Assert.Equal("", origin.Subtype);
    }

    [Fact]
    public void Definition_Title_MatchesTheCatalogedDiagramName()
    {
        Assert.Equal(
            "Deployment (supplementary)",
            Diagram.Definition.Title);
    }
}
