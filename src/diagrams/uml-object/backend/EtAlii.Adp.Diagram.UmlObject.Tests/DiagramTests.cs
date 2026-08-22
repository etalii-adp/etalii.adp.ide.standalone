using Xunit;

namespace EtAlii.Adp.Diagram.UmlObject.Tests;

public class DiagramTests
{
    [Fact]
    public void Definition_Origin_MatchesTheCatalogedOriginTag()
    {
        var origin = Diagram.Definition.Origin;

        Assert.Equal("uml", origin.Vendor);
        Assert.Equal("object", origin.Type);
        Assert.Equal("", origin.Subtype);
    }

    [Fact]
    public void Definition_Title_MatchesTheCatalogedDiagramName()
    {
        Assert.Equal(
            "Object diagram",
            Diagram.Definition.Title);
    }
}
