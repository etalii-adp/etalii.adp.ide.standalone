using Xunit;

namespace EtAlii.Adp.Diagram.ZachmanMatrix.Tests;

public class DiagramTests
{
    [Fact]
    public void Definition_Origin_MatchesTheCatalogedOriginTag()
    {
        var origin = Diagram.Definition.Origin;

        Assert.Equal("zachman", origin.Vendor);
        Assert.Equal("matrix", origin.Type);
        Assert.Equal("", origin.Subtype);
    }

    [Fact]
    public void Definition_Title_MatchesTheCatalogedDiagramName()
    {
        Assert.Equal(
            "Zachman Framework matrix",
            Diagram.Definition.Title);
    }
}
