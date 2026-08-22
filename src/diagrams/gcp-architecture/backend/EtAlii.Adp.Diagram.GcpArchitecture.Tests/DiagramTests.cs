using Xunit;

namespace EtAlii.Adp.Diagram.GcpArchitecture.Tests;

public class DiagramTests
{
    [Fact]
    public void Definition_Origin_MatchesTheCatalogedOriginTag()
    {
        var origin = EtAlii.Adp.Diagram.GcpArchitecture.Diagram.Definition.Origin;

        Assert.Equal("gcp", origin.Vendor);
        Assert.Equal("architecture", origin.Type);
        Assert.Equal("", origin.Subtype);
    }

    [Fact]
    public void Definition_Title_MatchesTheCatalogedDiagramName()
    {
        Assert.Equal(
            "GCP architecture diagram",
            EtAlii.Adp.Diagram.GcpArchitecture.Diagram.Definition.Title);
    }
}
