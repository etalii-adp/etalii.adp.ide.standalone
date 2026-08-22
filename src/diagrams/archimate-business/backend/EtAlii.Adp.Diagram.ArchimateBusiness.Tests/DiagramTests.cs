using Xunit;

namespace EtAlii.Adp.Diagram.ArchimateBusiness.Tests;

public class DiagramTests
{
    [Fact]
    public void Definition_Origin_MatchesTheCatalogedOriginTag()
    {
        var origin = EtAlii.Adp.Diagram.ArchimateBusiness.Diagram.Definition.Origin;

        Assert.Equal("archimate", origin.Vendor);
        Assert.Equal("business", origin.Type);
        Assert.Equal("", origin.Subtype);
    }

    [Fact]
    public void Definition_Title_MatchesTheCatalogedDiagramName()
    {
        Assert.Equal(
            "ArchiMate — Business layer",
            EtAlii.Adp.Diagram.ArchimateBusiness.Diagram.Definition.Title);
    }
}
