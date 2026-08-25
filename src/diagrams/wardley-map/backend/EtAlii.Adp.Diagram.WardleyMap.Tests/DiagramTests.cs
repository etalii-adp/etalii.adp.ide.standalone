using Xunit;

namespace EtAlii.Adp.Diagram.WardleyMap.Tests;

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
        Assert.Equal("wardley", origin.Vendor);
        Assert.Equal("map", origin.Type);
        Assert.Equal("", origin.Subtype);
    }

    [Fact]
    public void Definition_Title_MatchesTheCatalogedDiagramName()
    {
        // Arrange, act and assert.
        Assert.Equal(
            "Wardley Map",
            Definition.Title);
    }

    [Fact]
    public void Definition_Extension_IsTheOnlineWardleyMapsExtension()
    {
        // Act.
        var extension = Definition.Extension;

        // Assert.
        Assert.Equal(".owm", extension);
        Assert.Equal(Diagram.DocumentExtension, extension);
    }

    [Fact]
    public void Definition_HasDocumentSibling_BecauseTheBodyLivesInTheOwmFile()
    {
        // Act and assert. Core names the sibling by construction from the extension
        // (Requirement 2.2), so this is what makes `<name>.owm` findable at all.
        Assert.True(Definition.HasDocumentSibling);
    }

    [Fact]
    public void Definition_MimeType_IsTheFirstLineOfAnAdpRegistration()
    {
        // Act and assert. `create-diagram-file` Requirement 3.2 derives this from the origin,
        // and Requirement 1.3 says a created `.adp` carries exactly this on its first line.
        Assert.Equal("wardley/map", Definition.Origin.MimeType);
    }
}
