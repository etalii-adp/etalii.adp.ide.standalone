using Xunit;

namespace EtAlii.Adp.Diagram.C4Code.Tests;

public class DiagramTests
{
    [Fact]
    public void Definition_Origin_MatchesTheCatalogedOriginTag()
    {
        var origin = EtAlii.Adp.Diagram.C4Code.Diagram.Definition.Origin;

        Assert.Equal("c4", origin.Vendor);
        Assert.Equal("code", origin.Type);
        Assert.Equal("", origin.Subtype);
    }

    [Fact]
    public void Definition_Title_MatchesTheCatalogedDiagramName()
    {
        Assert.Equal(
            "Code (optional)",
            EtAlii.Adp.Diagram.C4Code.Diagram.Definition.Title);
    }
}
