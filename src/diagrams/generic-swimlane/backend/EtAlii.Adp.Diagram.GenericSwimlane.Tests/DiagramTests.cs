using Xunit;

namespace EtAlii.Adp.Diagram.GenericSwimlane.Tests;

public class DiagramTests
{
    [Fact]
    public void Definition_Origin_MatchesTheCatalogedOriginTag()
    {
        var origin = Diagram.Definition.Origin;

        Assert.Equal("generic", origin.Vendor);
        Assert.Equal("swimlane", origin.Type);
        Assert.Equal("", origin.Subtype);
    }

    [Fact]
    public void Definition_Title_MatchesTheCatalogedDiagramName()
    {
        Assert.Equal(
            "Swimlane diagram",
            Diagram.Definition.Title);
    }
}
