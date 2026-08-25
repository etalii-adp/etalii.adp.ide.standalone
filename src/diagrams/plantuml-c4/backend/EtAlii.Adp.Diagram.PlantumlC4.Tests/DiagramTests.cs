using Xunit;

namespace EtAlii.Adp.Diagram.PlantumlC4.Tests;

public class DiagramTests
{
    [Fact]
    public void Definition_Origin_MatchesTheCatalogedOriginTag()
    {
        // Act.
        var origin = Diagram.Definition.Origin;

        // Assert.
        Assert.Equal("plantuml", origin.Vendor);
        Assert.Equal("c4", origin.Type);
        Assert.Equal("", origin.Subtype);
    }

    [Fact]
    public void Definition_Title_MatchesTheCatalogedDiagramName()
    {
        // Arrange, act and assert.
        Assert.Equal(
            "C4 diagram, via C4-PlantUML",
            Diagram.Definition.Title);
    }
}
