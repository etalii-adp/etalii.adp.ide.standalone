using Xunit;

namespace EtAlii.Adp.Diagram.DddContextMap.Tests;

public class DiagramTests
{
    [Fact]
    public void Definition_Origin_MatchesTheCatalogedOriginTag()
    {
        // Act.
        var origin = Diagram.Definition.Origin;

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
            Diagram.Definition.Title);
    }
}
