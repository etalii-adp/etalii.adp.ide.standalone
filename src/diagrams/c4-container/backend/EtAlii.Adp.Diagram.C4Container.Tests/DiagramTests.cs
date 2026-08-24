using Xunit;

namespace EtAlii.Adp.Diagram.C4Container.Tests;

public class DiagramTests
{
    [Fact]
    public void Definition_Origin_MatchesTheCatalogedOriginTag()
    {
        var origin = Diagram.Definition.Origin;

        Assert.Equal("c4", origin.Vendor);
        Assert.Equal("container", origin.Type);
        Assert.Equal("", origin.Subtype);
    }

    [Fact]
    public void Definition_Title_MatchesTheCatalogedDiagramName()
    {
        Assert.Equal(
            "Container",
            Diagram.Definition.Title);
    }

    [Fact]
    public void Definition_KeepsItsBody_InAStructurizrDslSibling()
    {
        // Several C4 diagrams can share one model document, so every C4 type declares the
        // same extension and core names the sibling by construction (c4-diagrams
        // Requirement 2.2).
        Assert.Equal(".dsl", Diagram.Definition.Extension);
        Assert.True(Diagram.Definition.HasDocumentSibling);
    }
}
