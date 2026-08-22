using Xunit;

namespace EtAlii.Adp.Diagram.MermaidArchitecture.Tests;

public class DiagramTests
{
    [Fact]
    public void Definition_Origin_MatchesTheCatalogedOriginTag()
    {
        var origin = EtAlii.Adp.Diagram.MermaidArchitecture.Diagram.Definition.Origin;

        Assert.Equal("mermaid", origin.Vendor);
        Assert.Equal("architecture", origin.Type);
        Assert.Equal("", origin.Subtype);
    }

    [Fact]
    public void Definition_Title_MatchesTheCatalogedDiagramName()
    {
        Assert.Equal(
            "Architecture diagram",
            EtAlii.Adp.Diagram.MermaidArchitecture.Diagram.Definition.Title);
    }
}
