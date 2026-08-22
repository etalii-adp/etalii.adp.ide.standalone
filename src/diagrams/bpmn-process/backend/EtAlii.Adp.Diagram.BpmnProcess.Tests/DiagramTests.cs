using Xunit;

namespace EtAlii.Adp.Diagram.BpmnProcess.Tests;

public class DiagramTests
{
    [Fact]
    public void Definition_Origin_MatchesTheCatalogedOriginTag()
    {
        var origin = Diagram.Definition.Origin;

        Assert.Equal("bpmn", origin.Vendor);
        Assert.Equal("process", origin.Type);
        Assert.Equal("", origin.Subtype);
    }

    [Fact]
    public void Definition_Title_MatchesTheCatalogedDiagramName()
    {
        Assert.Equal(
            "BPMN process diagram",
            Diagram.Definition.Title);
    }
}
