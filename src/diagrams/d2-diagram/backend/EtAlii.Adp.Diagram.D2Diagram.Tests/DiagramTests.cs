using Xunit;

namespace EtAlii.Adp.Diagram.D2Diagram.Tests;

public class DiagramTests
{
    [Fact]
    public void Definition_Origin_MatchesTheCatalogedOriginTag()
    {
        var origin = EtAlii.Adp.Diagram.D2Diagram.Diagram.Definition.Origin;

        Assert.Equal("d2", origin.Vendor);
        Assert.Equal("diagram", origin.Type);
        Assert.Equal("", origin.Subtype);
    }

    [Fact]
    public void Definition_Title_MatchesTheCatalogedDiagramName()
    {
        Assert.Equal(
            "General-purpose declarative diagram",
            EtAlii.Adp.Diagram.D2Diagram.Diagram.Definition.Title);
    }
}
