using Xunit;

namespace EtAlii.Adp.Diagram.MermaidC4.Tests;

public class DiagramTests
{
    [Fact]
    public void Definition_Origin_MatchesTheCatalogedOriginTag()
    {
        var origin = EtAlii.Adp.Diagram.MermaidC4.Diagram.Definition.Origin;

        Assert.Equal("mermaid", origin.Vendor);
        Assert.Equal("c4", origin.Type);
        Assert.Equal("", origin.Subtype);
    }

    [Fact]
    public void Definition_Title_MatchesTheCatalogedDiagramName()
    {
        Assert.Equal(
            "C4 diagram (subset)",
            EtAlii.Adp.Diagram.MermaidC4.Diagram.Definition.Title);
    }
}
