using Xunit;

namespace EtAlii.Adp.Diagram.C4Code.Tests;

public class DiagramTests
{
    [Fact]
    public void Definition_Origin_MatchesTheCatalogedOriginTag()
    {
        var origin = Diagram.Definition.Origin;

        Assert.Equal("c4", origin.Vendor);
        Assert.Equal("code", origin.Type);
        Assert.Equal("", origin.Subtype);
    }

    [Fact]
    public void Definition_Title_MatchesTheCatalogedDiagramName()
    {
        Assert.Equal(
            "Code (optional)",
            Diagram.Definition.Title);
    }

    [Fact]
    public void Definition_ClaimsNoExtension_UnlikeItsSixSiblings()
    {
        // The other C4 types keep their model in a Structurizr DSL document; the code level
        // does not, because the DSL declares no code view and C4 specifies UML class or ER
        // notation for it. Claiming .dsl also made this - the one C4 type that cannot open
        // anything - the first claimant of that extension in catalog order, which broke
        // opening a .dsl with no .adp beside it (c4-diagrams Requirement 11, deviating from
        // Requirement 2.2).
        Assert.Equal("", Diagram.Definition.Extension);
        Assert.False(Diagram.Definition.HasDocumentSibling);
    }
}
