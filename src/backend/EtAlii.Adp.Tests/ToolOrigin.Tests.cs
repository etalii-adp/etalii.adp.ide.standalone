using Xunit;

namespace EtAlii.Adp.Tests;

public class ToolOriginTests
{
    [Fact]
    public void Constructor_WithoutASubtype_DefaultsSubtypeToEmpty()
    {
        // Act.
        var origin = new ToolOrigin("uml", "class");

        // Assert.
        Assert.Equal("uml", origin.Vendor);
        Assert.Equal("class", origin.Type);
        Assert.Equal("", origin.Subtype);
    }

    [Fact]
    public void Constructor_WithASubtype_KeepsIt()
    {
        // Act.
        var origin = new ToolOrigin("c4", "component", "code");

        // Assert.
        Assert.Equal("code", origin.Subtype);
    }

    [Fact]
    public void Equality_ForTwoOriginsWithTheSameValues_HoldsByValue()
    {
        // Arrange, act and assert.
        Assert.Equal(new ToolOrigin("uml", "class"), new ToolOrigin("uml", "class"));
    }

    [Fact]
    public void MimeType_WithoutASubtype_IsVendorSlashType()
    {
        // Arrange, act and assert.
        Assert.Equal("freeplane/mindmap", new ToolOrigin("freeplane", "mindmap").MimeType);
    }

    [Fact]
    public void MimeType_WithASubtype_AppendsItWithAPlusSoThereIsOnlyOneSlash()
    {
        // Act.
        var mimeType = new ToolOrigin("plantuml", "uml", "sequence").MimeType;

        // Assert.
        Assert.Equal("plantuml/uml+sequence", mimeType);
        Assert.Equal(1, mimeType.Count(character => character == '/'));
    }

    [Fact]
    public void MimeType_WithoutASubtype_MatchesTheKeyTheCatalogUses()
    {
        // Act.
        var origin = new ToolOrigin("c4", "context");

        // Assert.
        Assert.Equal(origin.Key, origin.MimeType);
    }

    [Fact]
    public void MimeType_WithASubtype_DiffersFromTheKey()
    {
        // Act.
        // The key identifies a choice inside ADP; the MIME type is what a file may claim to be.
        var origin = new ToolOrigin("c4", "component", "code");

        // Assert.
        Assert.Equal("c4/component/code", origin.Key);
        Assert.Equal("c4/component+code", origin.MimeType);
    }

    [Theory]
    [InlineData("etalii/knowledge", "etalii", "knowledge", "")]
    [InlineData("uml/class", "uml", "class", "")]
    [InlineData("c4/system/context", "c4", "system", "context")]
    public void TryParse_ReadsAKey_AsTheOriginItNames(string key, string vendor, string type, string subtype)
    {
        // Act.
        var origin = ToolOrigin.TryParse(key);

        // Assert: the origin, which writes the same key back.
        Assert.Equal(new ToolOrigin(vendor, type, subtype), origin);
        Assert.Equal(key, origin!.Key);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("knowledge")]
    [InlineData("etalii/")]
    [InlineData("/knowledge")]
    [InlineData("a/b/c/d")]
    public void TryParse_NamesNoOrigin_ForATextThatIsNotAKey(string? key)
    {
        // Act and assert.
        Assert.Null(ToolOrigin.TryParse(key));
    }
}
