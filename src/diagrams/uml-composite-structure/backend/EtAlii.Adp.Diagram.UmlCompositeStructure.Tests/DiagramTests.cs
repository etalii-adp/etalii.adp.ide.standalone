using Xunit;

namespace EtAlii.Adp.Diagram.UmlCompositeStructure.Tests;

public class DiagramTests
{
    [Fact]
    public void Definition_Origin_MatchesTheCatalogedOriginTag()
    {
        // Act.
        var origin = Diagram.Definition.Origin;

        // Assert.
        Assert.Equal("uml", origin.Vendor);
        Assert.Equal("composite-structure", origin.Type);
        Assert.Equal("", origin.Subtype);
    }

    [Fact]
    public void Definition_Title_MatchesTheCatalogedDiagramName()
    {
        // Arrange, act and assert.
        Assert.Equal(
            "Composite structure diagram",
            Diagram.Definition.Title);
    }
}
