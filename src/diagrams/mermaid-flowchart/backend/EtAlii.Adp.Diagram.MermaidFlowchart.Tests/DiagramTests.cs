using Xunit;

namespace EtAlii.Adp.Diagram.MermaidFlowchart.Tests;

public class DiagramTests
{
    [Fact]
    public void Definition_Origin_MatchesTheCatalogedOriginTag()
    {
        // Act.
        var origin = Diagram.Definition.Origin;

        // Assert.
        Assert.Equal("mermaid", origin.Vendor);
        Assert.Equal("flowchart", origin.Type);
        Assert.Equal("", origin.Subtype);
    }

    [Fact]
    public void Definition_Title_MatchesTheCatalogedDiagramName()
    {
        // Arrange, act and assert.
        Assert.Equal(
            "Flowchart",
            Diagram.Definition.Title);
    }
}
