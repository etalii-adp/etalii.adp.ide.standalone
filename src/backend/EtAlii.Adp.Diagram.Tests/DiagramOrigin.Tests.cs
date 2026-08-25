using Xunit;

namespace EtAlii.Adp.Diagram.Tests;

public class DiagramOriginTests
{
    [Fact]
    public void Constructor_WithoutASubtype_DefaultsSubtypeToEmpty()
    {
        // Act.
        var origin = new DiagramOrigin("uml", "class");

        // Assert.
        Assert.Equal("uml", origin.Vendor);
        Assert.Equal("class", origin.Type);
        Assert.Equal("", origin.Subtype);
    }

    [Fact]
    public void Constructor_WithASubtype_KeepsIt()
    {
        // Act.
        var origin = new DiagramOrigin("c4", "component", "code");

        // Assert.
        Assert.Equal("code", origin.Subtype);
    }

    [Fact]
    public void Equality_ForTwoOriginsWithTheSameValues_HoldsByValue()
    {
        // Arrange, act and assert.
        Assert.Equal(new DiagramOrigin("uml", "class"), new DiagramOrigin("uml", "class"));
    }

    [Fact]
    public void MimeType_WithoutASubtype_IsVendorSlashType()
    {
        // Arrange, act and assert.
        Assert.Equal("freeplane/mindmap", new DiagramOrigin("freeplane", "mindmap").MimeType);
    }

    [Fact]
    public void MimeType_WithASubtype_AppendsItWithAPlusSoThereIsOnlyOneSlash()
    {
        // Act.
        var mimeType = new DiagramOrigin("plantuml", "uml", "sequence").MimeType;

        // Assert.
        Assert.Equal("plantuml/uml+sequence", mimeType);
        Assert.Equal(1, mimeType.Count(character => character == '/'));
    }

    [Fact]
    public void MimeType_WithoutASubtype_MatchesTheKeyTheCatalogUses()
    {
        // Act.
        var origin = new DiagramOrigin("c4", "context");

        // Assert.
        Assert.Equal(origin.Key, origin.MimeType);
    }

    [Fact]
    public void MimeType_WithASubtype_DiffersFromTheKey()
    {
        // Act.
        // The key identifies a choice inside ADP; the MIME type is what a file may claim to be.
        var origin = new DiagramOrigin("c4", "component", "code");

        // Assert.
        Assert.Equal("c4/component/code", origin.Key);
        Assert.Equal("c4/component+code", origin.MimeType);
    }
}
