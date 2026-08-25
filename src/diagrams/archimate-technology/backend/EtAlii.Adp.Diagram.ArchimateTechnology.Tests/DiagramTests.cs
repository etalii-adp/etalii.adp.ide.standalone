using Xunit;

namespace EtAlii.Adp.Diagram.ArchimateTechnology.Tests;

public class DiagramTests
{
    [Fact]
    public void Definition_Origin_MatchesTheCatalogedOriginTag()
    {
        // Act.
        var origin = Diagram.Definition.Origin;

        // Assert.
        Assert.Equal("archimate", origin.Vendor);
        Assert.Equal("technology", origin.Type);
        Assert.Equal("", origin.Subtype);
    }

    [Fact]
    public void Definition_Title_MatchesTheCatalogedDiagramName()
    {
        // Arrange, act and assert.
        Assert.Equal(
            "ArchiMate — Technology layer",
            Diagram.Definition.Title);
    }
}
