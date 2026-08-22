using Xunit;

namespace EtAlii.Adp.Diagram.PlantumlC4.Tests;

public class DiagramTests
{
    [Fact]
    public void Definition_Origin_MatchesTheCatalogedOriginTag()
    {
        var origin = EtAlii.Adp.Diagram.PlantumlC4.Diagram.Definition.Origin;

        Assert.Equal("plantuml", origin.Vendor);
        Assert.Equal("c4", origin.Type);
        Assert.Equal("", origin.Subtype);
    }

    [Fact]
    public void Definition_Title_MatchesTheCatalogedDiagramName()
    {
        Assert.Equal(
            "C4 diagram, via C4-PlantUML",
            EtAlii.Adp.Diagram.PlantumlC4.Diagram.Definition.Title);
    }
}
