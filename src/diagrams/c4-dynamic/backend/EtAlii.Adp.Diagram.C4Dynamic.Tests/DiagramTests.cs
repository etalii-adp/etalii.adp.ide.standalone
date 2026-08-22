using Xunit;

namespace EtAlii.Adp.Diagram.C4Dynamic.Tests;

public class DiagramTests
{
    [Fact]
    public void Definition_Origin_MatchesTheCatalogedOriginTag()
    {
        var origin = EtAlii.Adp.Diagram.C4Dynamic.Diagram.Definition.Origin;

        Assert.Equal("c4", origin.Vendor);
        Assert.Equal("dynamic", origin.Type);
        Assert.Equal("", origin.Subtype);
    }

    [Fact]
    public void Definition_Title_MatchesTheCatalogedDiagramName()
    {
        Assert.Equal(
            "Dynamic (supplementary)",
            EtAlii.Adp.Diagram.C4Dynamic.Diagram.Definition.Title);
    }
}
