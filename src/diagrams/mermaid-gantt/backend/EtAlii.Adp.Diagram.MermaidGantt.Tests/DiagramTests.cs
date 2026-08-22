using Xunit;

namespace EtAlii.Adp.Diagram.MermaidGantt.Tests;

public class DiagramTests
{
    [Fact]
    public void Definition_Origin_MatchesTheCatalogedOriginTag()
    {
        var origin = EtAlii.Adp.Diagram.MermaidGantt.Diagram.Definition.Origin;

        Assert.Equal("mermaid", origin.Vendor);
        Assert.Equal("gantt", origin.Type);
        Assert.Equal("", origin.Subtype);
    }

    [Fact]
    public void Definition_Title_MatchesTheCatalogedDiagramName()
    {
        Assert.Equal(
            "Gantt chart",
            EtAlii.Adp.Diagram.MermaidGantt.Diagram.Definition.Title);
    }
}
