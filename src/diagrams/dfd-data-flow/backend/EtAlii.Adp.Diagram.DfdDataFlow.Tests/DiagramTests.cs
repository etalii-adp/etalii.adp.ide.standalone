using Xunit;

namespace EtAlii.Adp.Diagram.DfdDataFlow.Tests;

public class DiagramTests
{
    [Fact]
    public void Definition_Origin_MatchesTheCatalogedOriginTag()
    {
        // Act.
        var origin = Diagram.Definition.Origin;

        // Assert.
        Assert.Equal("dfd", origin.Vendor);
        Assert.Equal("data-flow", origin.Type);
        Assert.Equal("", origin.Subtype);
    }

    [Fact]
    public void Definition_Title_MatchesTheCatalogedDiagramName()
    {
        // Arrange, act and assert.
        Assert.Equal(
            "Data Flow Diagram (Yourdon/DeMarco or Gane–Sarson notation)",
            Diagram.Definition.Title);
    }
}
