using Xunit;

namespace EtAlii.Adp.Diagram.PlantumlC4.Tests;

public class DiagramTests
{
    /// <summary>
    /// This module declares exactly one type, so Single() is the assertion as well as the
    /// accessor: if it ever grows a second, these tests fail rather than silently checking
    /// whichever one happened to be first.
    /// </summary>
    private static DiagramDefinition Definition => Assert.Single(Diagram.Definitions);

    [Fact]
    public void Definition_Origin_MatchesTheCatalogedOriginTag()
    {
        // Act.
        var origin = Definition.Origin;

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
            Definition.Title);
    }
}
