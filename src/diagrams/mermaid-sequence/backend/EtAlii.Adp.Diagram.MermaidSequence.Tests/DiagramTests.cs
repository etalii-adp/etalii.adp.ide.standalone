using Xunit;

namespace EtAlii.Adp.Diagram.MermaidSequence.Tests;

public class DiagramTests
{
    [Fact]
    public void Definition_Origin_MatchesTheCatalogedOriginTag()
    {
        var origin = EtAlii.Adp.Diagram.MermaidSequence.Diagram.Definition.Origin;

        Assert.Equal("mermaid", origin.Vendor);
        Assert.Equal("sequence", origin.Type);
        Assert.Equal("", origin.Subtype);
    }

    [Fact]
    public void Definition_Title_MatchesTheCatalogedDiagramName()
    {
        Assert.Equal(
            "Sequence diagram",
            EtAlii.Adp.Diagram.MermaidSequence.Diagram.Definition.Title);
    }
}
