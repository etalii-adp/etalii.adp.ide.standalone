using Xunit;

namespace EtAlii.Adp.Diagram.DddContextMap.Tests;

public class DiagramTests
{
    [Fact]
    public void Definition_Origin_MatchesTheCatalogedOriginTag()
    {
        var origin = EtAlii.Adp.Diagram.DddContextMap.Diagram.Definition.Origin;

        Assert.Equal("ddd", origin.Vendor);
        Assert.Equal("context-map", origin.Type);
        Assert.Equal("", origin.Subtype);
    }

    [Fact]
    public void Definition_Title_MatchesTheCatalogedDiagramName()
    {
        Assert.Equal(
            "Bounded Context / Context Map",
            EtAlii.Adp.Diagram.DddContextMap.Diagram.Definition.Title);
    }
}
