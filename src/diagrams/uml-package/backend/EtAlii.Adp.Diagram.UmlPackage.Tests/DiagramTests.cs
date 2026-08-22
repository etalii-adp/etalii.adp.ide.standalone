using Xunit;

namespace EtAlii.Adp.Diagram.UmlPackage.Tests;

public class DiagramTests
{
    [Fact]
    public void Definition_Origin_MatchesTheCatalogedOriginTag()
    {
        var origin = Diagram.Definition.Origin;

        Assert.Equal("uml", origin.Vendor);
        Assert.Equal("package", origin.Type);
        Assert.Equal("", origin.Subtype);
    }

    [Fact]
    public void Definition_Title_MatchesTheCatalogedDiagramName()
    {
        Assert.Equal(
            "Package diagram",
            Diagram.Definition.Title);
    }
}
