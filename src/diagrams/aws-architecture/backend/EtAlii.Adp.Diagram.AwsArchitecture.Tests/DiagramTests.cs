using Xunit;

namespace EtAlii.Adp.Diagram.AwsArchitecture.Tests;

public class DiagramTests
{
    [Fact]
    public void Definition_Origin_MatchesTheCatalogedOriginTag()
    {
        var origin = EtAlii.Adp.Diagram.AwsArchitecture.Diagram.Definition.Origin;

        Assert.Equal("aws", origin.Vendor);
        Assert.Equal("architecture", origin.Type);
        Assert.Equal("", origin.Subtype);
    }

    [Fact]
    public void Definition_Title_MatchesTheCatalogedDiagramName()
    {
        Assert.Equal(
            "AWS architecture diagram",
            EtAlii.Adp.Diagram.AwsArchitecture.Diagram.Definition.Title);
    }
}
