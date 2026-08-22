using Xunit;

namespace EtAlii.Adp.Diagram.StructurizrC4.Tests;

public class DiagramTests
{
    [Fact]
    public void Definition_Origin_MatchesTheCatalogedOriginTag()
    {
        var origin = Diagram.Definition.Origin;

        Assert.Equal("structurizr", origin.Vendor);
        Assert.Equal("c4", origin.Type);
        Assert.Equal("", origin.Subtype);
    }

    [Fact]
    public void Definition_Title_MatchesTheCatalogedDiagramName()
    {
        Assert.Equal(
            "C4 model (\"model once, view many\")",
            Diagram.Definition.Title);
    }
}
