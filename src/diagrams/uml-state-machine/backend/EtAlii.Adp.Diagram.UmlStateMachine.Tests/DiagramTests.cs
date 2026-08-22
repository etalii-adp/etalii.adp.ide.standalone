using Xunit;

namespace EtAlii.Adp.Diagram.UmlStateMachine.Tests;

public class DiagramTests
{
    [Fact]
    public void Definition_Origin_MatchesTheCatalogedOriginTag()
    {
        var origin = EtAlii.Adp.Diagram.UmlStateMachine.Diagram.Definition.Origin;

        Assert.Equal("uml", origin.Vendor);
        Assert.Equal("state-machine", origin.Type);
        Assert.Equal("", origin.Subtype);
    }

    [Fact]
    public void Definition_Title_MatchesTheCatalogedDiagramName()
    {
        Assert.Equal(
            "State machine diagram",
            EtAlii.Adp.Diagram.UmlStateMachine.Diagram.Definition.Title);
    }
}
