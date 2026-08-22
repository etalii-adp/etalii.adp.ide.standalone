using Xunit;

namespace EtAlii.Adp.Diagram.MermaidFlowchart.Tests;

public class DiagramTests
{
    [Fact]
    public void Definition_Origin_MatchesTheCatalogedOriginTag()
    {
        var origin = EtAlii.Adp.Diagram.MermaidFlowchart.Diagram.Definition.Origin;

        Assert.Equal("mermaid", origin.Vendor);
        Assert.Equal("flowchart", origin.Type);
        Assert.Equal("", origin.Subtype);
    }

    [Fact]
    public void Definition_Title_MatchesTheCatalogedDiagramName()
    {
        Assert.Equal(
            "Flowchart",
            EtAlii.Adp.Diagram.MermaidFlowchart.Diagram.Definition.Title);
    }
}
