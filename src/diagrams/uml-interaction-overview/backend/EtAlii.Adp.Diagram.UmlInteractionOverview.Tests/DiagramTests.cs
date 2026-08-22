using Xunit;

namespace EtAlii.Adp.Diagram.UmlInteractionOverview.Tests;

public class DiagramTests
{
    [Fact]
    public void Definition_Origin_MatchesTheCatalogedOriginTag()
    {
        var origin = EtAlii.Adp.Diagram.UmlInteractionOverview.Diagram.Definition.Origin;

        Assert.Equal("uml", origin.Vendor);
        Assert.Equal("interaction-overview", origin.Type);
        Assert.Equal("", origin.Subtype);
    }

    [Fact]
    public void Definition_Title_MatchesTheCatalogedDiagramName()
    {
        Assert.Equal(
            "Interaction overview diagram",
            EtAlii.Adp.Diagram.UmlInteractionOverview.Diagram.Definition.Title);
    }
}
