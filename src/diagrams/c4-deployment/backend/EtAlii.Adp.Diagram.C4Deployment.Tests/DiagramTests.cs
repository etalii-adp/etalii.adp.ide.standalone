using Xunit;

namespace EtAlii.Adp.Diagram.C4Deployment.Tests;

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
        Assert.Equal("deployment", origin.Type);
        Assert.Equal("", origin.Subtype);
    }

    [Fact]
    public void Definition_Title_MatchesTheCatalogedDiagramName()
    {
        // Arrange, act and assert.
        Assert.Equal(
            "Deployment (supplementary)",
            Definition.Title);
    }

    [Fact]
    public void Definition_KeepsItsBody_InAStructurizrDslSibling()
    {
        // Arrange, act and assert.
        // Several C4 diagrams can share one model document, so every C4 type declares the
        // same extension and core names the sibling by construction (c4-diagrams
        // Requirement 2.2).
        Assert.Equal(".dsl", Definition.Extension);
        Assert.True(Definition.HasDocumentSibling);
    }
}
