using Xunit;

namespace EtAlii.Adp.Diagram.ArchimateImplementationMigration.Tests;

public class DiagramTests
{
    [Fact]
    public void Definition_Origin_MatchesTheCatalogedOriginTag()
    {
        var origin = EtAlii.Adp.Diagram.ArchimateImplementationMigration.Diagram.Definition.Origin;

        Assert.Equal("archimate", origin.Vendor);
        Assert.Equal("implementation-migration", origin.Type);
        Assert.Equal("", origin.Subtype);
    }

    [Fact]
    public void Definition_Title_MatchesTheCatalogedDiagramName()
    {
        Assert.Equal(
            "ArchiMate — Implementation & Migration layer",
            EtAlii.Adp.Diagram.ArchimateImplementationMigration.Diagram.Definition.Title);
    }
}
