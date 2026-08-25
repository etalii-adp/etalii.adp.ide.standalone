using Xunit;

namespace EtAlii.Adp.Diagram.C4Context.Tests;

public class DiagramTests
{
    [Fact]
    public void Definition_Origin_MatchesTheCatalogedOriginTag()
    {
        // Act.
        var origin = Diagram.Definition.Origin;

        // Assert.
        Assert.Equal("c4", origin.Vendor);
        Assert.Equal("context", origin.Type);
        Assert.Equal("", origin.Subtype);
    }

    [Fact]
    public void Definition_Title_MatchesTheCatalogedDiagramName()
    {
        // Arrange, act and assert.
        Assert.Equal(
            "System Context",
            Diagram.Definition.Title);
    }

    [Fact]
    public void Definition_KeepsItsBody_InAStructurizrDslSibling()
    {
        // Arrange, act and assert.
        // Several C4 diagrams can share one model document, so every C4 type declares the
        // same extension and core names the sibling by construction (c4-diagrams
        // Requirement 2.2).
        Assert.Equal(".dsl", Diagram.Definition.Extension);
        Assert.True(Diagram.Definition.HasDocumentSibling);
    }
}
