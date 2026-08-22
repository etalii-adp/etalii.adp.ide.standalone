using Xunit;

namespace EtAlii.Adp.Diagram.Tests;

public class DiagramOriginTests
{
    [Fact]
    public void Constructor_WithoutASubtype_DefaultsSubtypeToEmpty()
    {
        var origin = new DiagramOrigin("uml", "class");

        Assert.Equal("uml", origin.Vendor);
        Assert.Equal("class", origin.Type);
        Assert.Equal("", origin.Subtype);
    }

    [Fact]
    public void Constructor_WithASubtype_KeepsIt()
    {
        var origin = new DiagramOrigin("c4", "component", "code");

        Assert.Equal("code", origin.Subtype);
    }

    [Fact]
    public void Equality_ForTwoOriginsWithTheSameValues_HoldsByValue()
    {
        Assert.Equal(new DiagramOrigin("uml", "class"), new DiagramOrigin("uml", "class"));
    }

    [Fact]
    public void MimeType_WithoutASubtype_IsVendorSlashType()
    {
        Assert.Equal("freeplane/mindmap", new DiagramOrigin("freeplane", "mindmap").MimeType);
    }

    [Fact]
    public void MimeType_WithASubtype_AppendsItWithAPlusSoThereIsOnlyOneSlash()
    {
        var mimeType = new DiagramOrigin("plantuml", "uml", "sequence").MimeType;

        Assert.Equal("plantuml/uml+sequence", mimeType);
        Assert.Equal(1, mimeType.Count(character => character == '/'));
    }

    [Fact]
    public void MimeType_WithoutASubtype_MatchesTheKeyTheCatalogUses()
    {
        var origin = new DiagramOrigin("c4", "context");

        Assert.Equal(origin.Key, origin.MimeType);
    }

    [Fact]
    public void MimeType_WithASubtype_DiffersFromTheKey()
    {
        // The key identifies a choice inside ADP; the MIME type is what a file may claim to be.
        var origin = new DiagramOrigin("c4", "component", "code");

        Assert.Equal("c4/component/code", origin.Key);
        Assert.Equal("c4/component+code", origin.MimeType);
    }
}
