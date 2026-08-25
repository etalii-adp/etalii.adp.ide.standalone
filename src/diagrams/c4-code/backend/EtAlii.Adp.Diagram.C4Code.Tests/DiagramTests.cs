using Xunit;

namespace EtAlii.Adp.Diagram.C4Code.Tests;

public class DiagramTests
{
    /// <summary>
    /// This module declares exactly one type, so Single() is the assertion as well as the
    /// accessor: if it ever grows a second, these tests fail rather than silently checking
    /// whichever one happened to be first.
    /// </summary>
    private static DiagramDefinition Definition => Assert.Single(Diagram.Definitions);

    [Fact]
    public void Definition_Origin_MatchesTheCatalogedOriginTag()
    {
        // Act.
        var origin = Definition.Origin;

        // Assert.
        Assert.Equal("c4", origin.Vendor);
        Assert.Equal("code", origin.Type);
        Assert.Equal("", origin.Subtype);
    }

    [Fact]
    public void Definition_Title_MatchesTheCatalogedDiagramName()
    {
        // Arrange, act and assert.
        Assert.Equal(
            "Code (optional)",
            Definition.Title);
    }

    [Fact]
    public void Definition_ClaimsNoExtension_UnlikeItsSixSiblings()
    {
        // Arrange, act and assert.
        // The other C4 types keep their model in a Structurizr DSL document; the code level
        // does not, because the DSL declares no code view and C4 specifies UML class or ER
        // notation for it. Claiming .dsl also made this - the one C4 type that cannot open
        // anything - the first claimant of that extension in catalog order, which broke
        // opening a .dsl with no .adp beside it (c4-diagrams Requirement 11, deviating from
        // Requirement 2.2).
        Assert.Equal("", Definition.Extension);
        Assert.False(Definition.HasDocumentSibling);
    }
}
