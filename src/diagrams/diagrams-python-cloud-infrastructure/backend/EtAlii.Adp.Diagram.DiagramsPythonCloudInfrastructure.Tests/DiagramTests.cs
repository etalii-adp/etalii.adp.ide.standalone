using Xunit;

namespace EtAlii.Adp.Diagram.DiagramsPythonCloudInfrastructure.Tests;

public class DiagramTests
{
    [Fact]
    public void Definition_Origin_MatchesTheCatalogedOriginTag()
    {
        var origin = Diagram.Definition.Origin;

        Assert.Equal("diagrams-python", origin.Vendor);
        Assert.Equal("cloud-infrastructure", origin.Type);
        Assert.Equal("", origin.Subtype);
    }

    [Fact]
    public void Definition_Title_MatchesTheCatalogedDiagramName()
    {
        Assert.Equal(
            "Cloud/infrastructure diagram with official-style vendor icons",
            Diagram.Definition.Title);
    }
}
