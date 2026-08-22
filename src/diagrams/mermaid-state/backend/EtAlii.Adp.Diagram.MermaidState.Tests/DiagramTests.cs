using Xunit;

namespace EtAlii.Adp.Diagram.MermaidState.Tests;

public class DiagramTests
{
    [Fact]
    public void Definition_Origin_MatchesTheCatalogedOriginTag()
    {
        var origin = Diagram.Definition.Origin;

        Assert.Equal("mermaid", origin.Vendor);
        Assert.Equal("state", origin.Type);
        Assert.Equal("", origin.Subtype);
    }

    [Fact]
    public void Definition_Title_MatchesTheCatalogedDiagramName()
    {
        Assert.Equal(
            "State diagram",
            Diagram.Definition.Title);
    }
}
