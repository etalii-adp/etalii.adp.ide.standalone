using Xunit;

namespace EtAlii.Adp.Diagram.DddEventStorming.Tests;

public class DiagramTests
{
    [Fact]
    public void Definition_Origin_MatchesTheCatalogedOriginTag()
    {
        // Act.
        var origin = Diagram.Definition.Origin;

        // Assert.
        Assert.Equal("ddd", origin.Vendor);
        Assert.Equal("event-storming", origin.Type);
        Assert.Equal("", origin.Subtype);
    }

    [Fact]
    public void Definition_Title_MatchesTheCatalogedDiagramName()
    {
        // Arrange, act and assert.
        Assert.Equal(
            "Event Storming",
            Diagram.Definition.Title);
    }
}
