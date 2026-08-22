using Xunit;

namespace EtAlii.Adp.Diagram.AzureArchitecture.Tests;

public class DiagramTests
{
    [Fact]
    public void Definition_Origin_MatchesTheCatalogedOriginTag()
    {
        var origin = EtAlii.Adp.Diagram.AzureArchitecture.Diagram.Definition.Origin;

        Assert.Equal("azure", origin.Vendor);
        Assert.Equal("architecture", origin.Type);
        Assert.Equal("", origin.Subtype);
    }

    [Fact]
    public void Definition_Title_MatchesTheCatalogedDiagramName()
    {
        Assert.Equal(
            "Azure architecture diagram",
            EtAlii.Adp.Diagram.AzureArchitecture.Diagram.Definition.Title);
    }
}
