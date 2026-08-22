using Xunit;

namespace EtAlii.Adp.Diagram.IsoFlowchart.Tests;

public class DiagramTests
{
    [Fact]
    public void Definition_Origin_MatchesTheCatalogedOriginTag()
    {
        var origin = EtAlii.Adp.Diagram.IsoFlowchart.Diagram.Definition.Origin;

        Assert.Equal("iso", origin.Vendor);
        Assert.Equal("flowchart", origin.Type);
        Assert.Equal("", origin.Subtype);
    }

    [Fact]
    public void Definition_Title_MatchesTheCatalogedDiagramName()
    {
        Assert.Equal(
            "Flowchart",
            EtAlii.Adp.Diagram.IsoFlowchart.Diagram.Definition.Title);
    }
}
