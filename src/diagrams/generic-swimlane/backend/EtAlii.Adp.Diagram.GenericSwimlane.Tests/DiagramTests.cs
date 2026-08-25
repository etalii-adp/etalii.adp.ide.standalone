using Xunit;

namespace EtAlii.Adp.Diagram.GenericSwimlane.Tests;

public class DiagramTests
{
    [Fact]
    public void Definition_Origin_MatchesTheCatalogedOriginTag()
    {
        // Act.
        var origin = Diagram.Definition.Origin;

        // Assert.
        Assert.Equal("generic", origin.Vendor);
        Assert.Equal("swimlane", origin.Type);
        Assert.Equal("", origin.Subtype);
    }

    [Fact]
    public void Definition_Title_MatchesTheCatalogedDiagramName()
    {
        // Arrange, act and assert.
        Assert.Equal(
            "Swimlane diagram",
            Diagram.Definition.Title);
    }
}
