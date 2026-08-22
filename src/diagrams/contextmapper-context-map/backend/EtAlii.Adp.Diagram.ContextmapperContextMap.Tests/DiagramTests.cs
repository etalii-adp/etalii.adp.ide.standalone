using Xunit;

namespace EtAlii.Adp.Diagram.ContextmapperContextMap.Tests;

public class DiagramTests
{
    [Fact]
    public void Definition_Origin_MatchesTheCatalogedOriginTag()
    {
        var origin = Diagram.Definition.Origin;

        Assert.Equal("contextmapper", origin.Vendor);
        Assert.Equal("context-map", origin.Type);
        Assert.Equal("", origin.Subtype);
    }

    [Fact]
    public void Definition_Title_MatchesTheCatalogedDiagramName()
    {
        Assert.Equal(
            "DDD Context Map, plus generated PlantUML/BPMN sketches",
            Diagram.Definition.Title);
    }
}
