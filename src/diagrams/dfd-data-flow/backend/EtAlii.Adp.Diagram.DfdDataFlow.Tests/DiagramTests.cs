using Xunit;

namespace EtAlii.Adp.Diagram.DfdDataFlow.Tests;

public class DiagramTests
{
    [Fact]
    public void Definition_Origin_MatchesTheCatalogedOriginTag()
    {
        var origin = EtAlii.Adp.Diagram.DfdDataFlow.Diagram.Definition.Origin;

        Assert.Equal("dfd", origin.Vendor);
        Assert.Equal("data-flow", origin.Type);
        Assert.Equal("", origin.Subtype);
    }

    [Fact]
    public void Definition_Title_MatchesTheCatalogedDiagramName()
    {
        Assert.Equal(
            "Data Flow Diagram (Yourdon/DeMarco or Gane–Sarson notation)",
            EtAlii.Adp.Diagram.DfdDataFlow.Diagram.Definition.Title);
    }
}
