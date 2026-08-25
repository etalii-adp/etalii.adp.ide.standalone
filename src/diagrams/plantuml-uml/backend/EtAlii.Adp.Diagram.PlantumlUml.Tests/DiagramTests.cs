using Xunit;

namespace EtAlii.Adp.Diagram.PlantumlUml.Tests;

public class DiagramTests
{
    [Fact]
    public void Definition_Origin_MatchesTheCatalogedOriginTag()
    {
        // Act.
        var origin = Diagram.Definition.Origin;

        // Assert.
        Assert.Equal("plantuml", origin.Vendor);
        Assert.Equal("uml", origin.Type);
        Assert.Equal("", origin.Subtype);
    }

    [Fact]
    public void Definition_Title_MatchesTheCatalogedDiagramName()
    {
        // Arrange, act and assert.
        Assert.Equal(
            "Full UML set (see section 1)",
            Diagram.Definition.Title);
    }
}
