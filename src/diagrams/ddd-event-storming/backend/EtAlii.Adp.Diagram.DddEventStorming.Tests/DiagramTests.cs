using Xunit;

namespace EtAlii.Adp.Diagram.DddEventStorming.Tests;

public class DiagramTests
{
    [Fact]
    public void Definition_Origin_MatchesTheCatalogedOriginTag()
    {
        var origin = EtAlii.Adp.Diagram.DddEventStorming.Diagram.Definition.Origin;

        Assert.Equal("ddd", origin.Vendor);
        Assert.Equal("event-storming", origin.Type);
        Assert.Equal("", origin.Subtype);
    }

    [Fact]
    public void Definition_Title_MatchesTheCatalogedDiagramName()
    {
        Assert.Equal(
            "Event Storming",
            EtAlii.Adp.Diagram.DddEventStorming.Diagram.Definition.Title);
    }
}
