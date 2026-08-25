using Xunit;

namespace EtAlii.Adp.Diagram.ErdEntityRelationship.Tests;

public class DiagramTests
{
    [Fact]
    public void Definition_Origin_MatchesTheCatalogedOriginTag()
    {
        // Act.
        var origin = Diagram.Definition.Origin;

        // Assert.
        Assert.Equal("erd", origin.Vendor);
        Assert.Equal("entity-relationship", origin.Type);
        Assert.Equal("", origin.Subtype);
    }

    [Fact]
    public void Definition_Title_MatchesTheCatalogedDiagramName()
    {
        // Arrange, act and assert.
        Assert.Equal(
            "Entity-Relationship Diagram (Chen or Crow's Foot notation)",
            Diagram.Definition.Title);
    }
}
