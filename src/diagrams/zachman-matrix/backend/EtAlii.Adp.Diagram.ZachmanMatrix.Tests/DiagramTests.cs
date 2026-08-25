using Xunit;

namespace EtAlii.Adp.Diagram.ZachmanMatrix.Tests;

public class DiagramTests
{
    [Fact]
    public void Definition_Origin_MatchesTheCatalogedOriginTag()
    {
        // Act.
        var origin = Diagram.Definition.Origin;

        // Assert.
        Assert.Equal("zachman", origin.Vendor);
        Assert.Equal("matrix", origin.Type);
        Assert.Equal("", origin.Subtype);
    }

    [Fact]
    public void Definition_Title_MatchesTheCatalogedDiagramName()
    {
        // Arrange, act and assert.
        Assert.Equal(
            "Zachman Framework matrix",
            Diagram.Definition.Title);
    }
}
