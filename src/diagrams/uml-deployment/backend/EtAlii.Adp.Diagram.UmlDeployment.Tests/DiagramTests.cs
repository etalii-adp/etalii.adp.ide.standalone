using Xunit;

namespace EtAlii.Adp.Diagram.UmlDeployment.Tests;

public class DiagramTests
{
    [Fact]
    public void Definition_Origin_MatchesTheCatalogedOriginTag()
    {
        var origin = EtAlii.Adp.Diagram.UmlDeployment.Diagram.Definition.Origin;

        Assert.Equal("uml", origin.Vendor);
        Assert.Equal("deployment", origin.Type);
        Assert.Equal("", origin.Subtype);
    }

    [Fact]
    public void Definition_Title_MatchesTheCatalogedDiagramName()
    {
        Assert.Equal(
            "Deployment diagram",
            EtAlii.Adp.Diagram.UmlDeployment.Diagram.Definition.Title);
    }
}
