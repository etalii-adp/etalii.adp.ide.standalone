using Xunit;

namespace EtAlii.Adp.Diagram.UmlCommunication.Tests;

public class DiagramTests
{
    [Fact]
    public void Definition_Origin_MatchesTheCatalogedOriginTag()
    {
        var origin = EtAlii.Adp.Diagram.UmlCommunication.Diagram.Definition.Origin;

        Assert.Equal("uml", origin.Vendor);
        Assert.Equal("communication", origin.Type);
        Assert.Equal("", origin.Subtype);
    }

    [Fact]
    public void Definition_Title_MatchesTheCatalogedDiagramName()
    {
        Assert.Equal(
            "Communication diagram",
            EtAlii.Adp.Diagram.UmlCommunication.Diagram.Definition.Title);
    }
}
