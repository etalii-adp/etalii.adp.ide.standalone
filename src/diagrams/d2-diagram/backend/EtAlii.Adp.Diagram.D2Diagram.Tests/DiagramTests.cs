using Xunit;

namespace EtAlii.Adp.Diagram.D2Diagram.Tests;

public class DiagramTests
{
    [Fact]
    public void Definition_Origin_MatchesTheCatalogedOriginTag()
    {
        // Act.
        var origin = Diagram.Definition.Origin;

        // Assert.
        Assert.Equal("d2", origin.Vendor);
        Assert.Equal("diagram", origin.Type);
        Assert.Equal("", origin.Subtype);
    }

    [Fact]
    public void Definition_Title_MatchesTheCatalogedDiagramName()
    {
        // Arrange, act and assert.
        Assert.Equal(
            "General-purpose declarative diagram",
            Diagram.Definition.Title);
    }
}
