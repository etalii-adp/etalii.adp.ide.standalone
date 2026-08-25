using Xunit;

namespace EtAlii.Adp.Diagram.DddContextMap.Tests;

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
        Assert.Equal("ddd", origin.Vendor);
        Assert.Equal("context-map", origin.Type);
        Assert.Equal("", origin.Subtype);
    }

    [Fact]
    public void Definition_Title_MatchesTheCatalogedDiagramName()
    {
        // Arrange, act and assert.
        Assert.Equal(
            "Bounded Context / Context Map",
            Definition.Title);
    }
}
