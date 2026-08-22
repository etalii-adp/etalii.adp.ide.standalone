using Xunit;

namespace EtAlii.Adp.Diagram.DddEventStorming.Tests;

public class DiagramTests
{
    [Fact]
    public void Definition_Origin_MatchesTheCatalogedOriginTag()
    {
        var origin = Diagram.Definition.Origin;

        Assert.Equal("ddd", origin.Vendor);
        Assert.Equal("event-storming", origin.Type);
        Assert.Equal("", origin.Subtype);
    }

    [Fact]
    public void Definition_Title_MatchesTheCatalogedDiagramName()
    {
        Assert.Equal(
            "Event Storming",
            Diagram.Definition.Title);
    }
}
