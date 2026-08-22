using Xunit;

namespace EtAlii.Adp.Diagram.ArchimateTechnology.Tests;

public class DiagramTests
{
    [Fact]
    public void Definition_Origin_MatchesTheCatalogedOriginTag()
    {
        var origin = EtAlii.Adp.Diagram.ArchimateTechnology.Diagram.Definition.Origin;

        Assert.Equal("archimate", origin.Vendor);
        Assert.Equal("technology", origin.Type);
        Assert.Equal("", origin.Subtype);
    }

    [Fact]
    public void Definition_Title_MatchesTheCatalogedDiagramName()
    {
        Assert.Equal(
            "ArchiMate — Technology layer",
            EtAlii.Adp.Diagram.ArchimateTechnology.Diagram.Definition.Title);
    }
}
