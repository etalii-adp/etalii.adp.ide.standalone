using Xunit;

namespace EtAlii.Adp.Diagram.UmlDeployment.Tests;

public class DiagramTests
{
    [Fact]
    public void Definition_Origin_MatchesTheCatalogedOriginTag()
    {
        var origin = Diagram.Definition.Origin;

        Assert.Equal("uml", origin.Vendor);
        Assert.Equal("deployment", origin.Type);
        Assert.Equal("", origin.Subtype);
    }

    [Fact]
    public void Definition_Title_MatchesTheCatalogedDiagramName()
    {
        Assert.Equal(
            "Deployment diagram",
            Diagram.Definition.Title);
    }
}
