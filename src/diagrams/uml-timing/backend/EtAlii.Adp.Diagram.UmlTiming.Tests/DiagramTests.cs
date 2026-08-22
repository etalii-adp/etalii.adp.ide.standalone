using Xunit;

namespace EtAlii.Adp.Diagram.UmlTiming.Tests;

public class DiagramTests
{
    [Fact]
    public void Definition_Origin_MatchesTheCatalogedOriginTag()
    {
        var origin = EtAlii.Adp.Diagram.UmlTiming.Diagram.Definition.Origin;

        Assert.Equal("uml", origin.Vendor);
        Assert.Equal("timing", origin.Type);
        Assert.Equal("", origin.Subtype);
    }

    [Fact]
    public void Definition_Title_MatchesTheCatalogedDiagramName()
    {
        Assert.Equal(
            "Timing diagram",
            EtAlii.Adp.Diagram.UmlTiming.Diagram.Definition.Title);
    }
}
