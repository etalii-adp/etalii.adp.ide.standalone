using Xunit;

namespace EtAlii.Adp.Diagram.MermaidEr.Tests;

public class DiagramTests
{
    [Fact]
    public void Definition_Origin_MatchesTheCatalogedOriginTag()
    {
        // Act.
        var origin = Diagram.Definition.Origin;

        // Assert.
        Assert.Equal("mermaid", origin.Vendor);
        Assert.Equal("er", origin.Type);
        Assert.Equal("", origin.Subtype);
    }

    [Fact]
    public void Definition_Title_MatchesTheCatalogedDiagramName()
    {
        // Arrange, act and assert.
        Assert.Equal(
            "Entity-Relationship diagram",
            Diagram.Definition.Title);
    }
}
