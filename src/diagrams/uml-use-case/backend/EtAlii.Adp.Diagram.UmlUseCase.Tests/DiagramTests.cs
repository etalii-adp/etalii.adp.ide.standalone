using Xunit;

namespace EtAlii.Adp.Diagram.UmlUseCase.Tests;

public class DiagramTests
{
    [Fact]
    public void Definition_Origin_MatchesTheCatalogedOriginTag()
    {
        var origin = EtAlii.Adp.Diagram.UmlUseCase.Diagram.Definition.Origin;

        Assert.Equal("uml", origin.Vendor);
        Assert.Equal("use-case", origin.Type);
        Assert.Equal("", origin.Subtype);
    }

    [Fact]
    public void Definition_Title_MatchesTheCatalogedDiagramName()
    {
        Assert.Equal(
            "Use case diagram",
            EtAlii.Adp.Diagram.UmlUseCase.Diagram.Definition.Title);
    }
}
