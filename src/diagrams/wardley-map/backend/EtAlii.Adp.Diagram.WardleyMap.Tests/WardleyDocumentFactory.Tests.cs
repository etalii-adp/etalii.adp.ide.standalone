using Xunit;

namespace EtAlii.Adp.Diagram.WardleyMap.Tests;

public class WardleyDocumentFactoryTests
{
    private readonly WardleyDocumentFactory _factory = new();

    [Fact]
    public void Origin_IsTheTypeThisWritesBodiesFor()
    {
        // Act.
        var origin = _factory.Origin;

        // Assert.
        Assert.Equal(Diagram.WardleyMap.Origin, origin);
    }

    [Fact]
    public void CreateEmptyDocument_CarriesTheFileBaseNameAsTheTitle()
    {
        // Act.
        var document = _factory.CreateEmptyDocument("map");

        // Assert.
        Assert.Equal("title map\n", document);
    }

    [Fact]
    public void CreateEmptyDocument_UsesTheNameGivenRatherThanAFixedOne()
    {
        // Arrange.
        const string baseName = "supply-chain";

        // Act.
        var document = _factory.CreateEmptyDocument(baseName);

        // Assert.
        Assert.Equal($"title {baseName}\n", document);
    }

    [Fact]
    public void CreateEmptyDocument_EndsWithALineFeedAndNoCarriageReturn()
    {
        // Act.
        var document = _factory.CreateEmptyDocument("map");

        // Assert. A file ADP creates has no existing style to preserve, and LF is what the
        // rest of this ecosystem writes. The corpus's crlf/lf pair covers preserving what a
        // file already uses; this covers what a new one starts as.
        Assert.EndsWith("\n", document, StringComparison.Ordinal);
        Assert.DoesNotContain('\r', document);
    }

    [Fact]
    public void CreateEmptyDocument_IsAValidMapRatherThanAnEmptyFile()
    {
        // Act.
        var document = _factory.CreateEmptyDocument("map");

        // Assert. Requirement 1.4 - a map with no components is a valid map, not an error, and
        // the body is never zero bytes. The stronger claim, that this parses through the real
        // OnlineWardleyMaps parser, is checked by Fixtures/certify.mjs rather than from here.
        Assert.NotEmpty(document);
        Assert.StartsWith("title ", document, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateEmptyDocument_RejectsANullName()
    {
        // Act and assert.
        Assert.Throws<ArgumentNullException>(() => _factory.CreateEmptyDocument(null!));
    }
}
