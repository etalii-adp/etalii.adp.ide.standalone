using Xunit;

namespace EtAlii.Adp.Diagram.BpmnProcess.Tests;

public class DiagramTests
{
    [Fact]
    public void Definition_Origin_MatchesTheCatalogedOriginTag()
    {
        // Act.
        var origin = Diagram.Definition.Origin;

        // Assert.
        Assert.Equal("bpmn", origin.Vendor);
        Assert.Equal("process", origin.Type);
        Assert.Equal("", origin.Subtype);
    }

    [Fact]
    public void Definition_Title_MatchesTheCatalogedDiagramName()
    {
        // Arrange, act and assert.
        Assert.Equal(
            "BPMN process diagram",
            Diagram.Definition.Title);
    }
}
