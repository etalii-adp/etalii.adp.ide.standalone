using Xunit;

namespace EtAlii.Adp.Diagram.ErdEntityRelationship.Tests;

public class DiagramTests
{
    [Fact]
    public void Definition_Origin_MatchesTheCatalogedOriginTag()
    {
        var origin = EtAlii.Adp.Diagram.ErdEntityRelationship.Diagram.Definition.Origin;

        Assert.Equal("erd", origin.Vendor);
        Assert.Equal("entity-relationship", origin.Type);
        Assert.Equal("", origin.Subtype);
    }

    [Fact]
    public void Definition_Title_MatchesTheCatalogedDiagramName()
    {
        Assert.Equal(
            "Entity-Relationship Diagram (Chen or Crow's Foot notation)",
            EtAlii.Adp.Diagram.ErdEntityRelationship.Diagram.Definition.Title);
    }
}
