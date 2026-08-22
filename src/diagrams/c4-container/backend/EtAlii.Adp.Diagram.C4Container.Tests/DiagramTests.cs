using Xunit;

namespace EtAlii.Adp.Diagram.C4Container.Tests;

public class DiagramTests
{
    [Fact]
    public void Definition_Origin_MatchesTheCatalogedOriginTag()
    {
        var origin = EtAlii.Adp.Diagram.C4Container.Diagram.Definition.Origin;

        Assert.Equal("c4", origin.Vendor);
        Assert.Equal("container", origin.Type);
        Assert.Equal("", origin.Subtype);
    }

    [Fact]
    public void Definition_Title_MatchesTheCatalogedDiagramName()
    {
        Assert.Equal(
            "Container",
            EtAlii.Adp.Diagram.C4Container.Diagram.Definition.Title);
    }
}
