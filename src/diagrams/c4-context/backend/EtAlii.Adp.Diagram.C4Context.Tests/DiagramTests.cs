using Xunit;

namespace EtAlii.Adp.Diagram.C4Context.Tests;

public class DiagramTests
{
    [Fact]
    public void Definition_Origin_MatchesTheCatalogedOriginTag()
    {
        var origin = EtAlii.Adp.Diagram.C4Context.Diagram.Definition.Origin;

        Assert.Equal("c4", origin.Vendor);
        Assert.Equal("context", origin.Type);
        Assert.Equal("", origin.Subtype);
    }

    [Fact]
    public void Definition_Title_MatchesTheCatalogedDiagramName()
    {
        Assert.Equal(
            "System Context",
            EtAlii.Adp.Diagram.C4Context.Diagram.Definition.Title);
    }
}
