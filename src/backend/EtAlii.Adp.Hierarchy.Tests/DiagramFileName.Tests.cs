using EtAlii.Adp.Common;
using EtAlii.Adp.Diagram;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Hierarchy.Tests;

public class DiagramFileNameTests : IDisposable
{
    private readonly string _folder;

    public DiagramFileNameTests()
    {
        _folder = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);
    }

    public void Dispose()
    {
        TestFolder.TryDelete(_folder);
    }

    private void Existing(string fileName) => File.WriteAllText(IoPath.Combine(_folder, fileName), "");

    [Fact]
    public void Suggest_ForAnOriginWithoutASubtype_UsesItsTypeSegment()
    {
        // Arrange, act and assert.
        Assert.Equal("mindmap", DiagramFileName.Suggest(new DiagramOrigin("freeplane", "mindmap"), _folder));
    }

    [Fact]
    public void Suggest_ForAnOriginWithASubtype_AppendsItWithAHyphen()
    {
        // Arrange, act and assert.
        Assert.Equal("uml-sequence", DiagramFileName.Suggest(new DiagramOrigin("plantuml", "uml", "sequence"), _folder));
    }

    [Fact]
    public void Suggest_WhenTheNameIsTaken_CountsUpToTheFirstFreeOne()
    {
        // Act.
        Existing("mindmap.adp");

        // Assert.
        Assert.Equal("mindmap-2", DiagramFileName.Suggest(new DiagramOrigin("freeplane", "mindmap"), _folder));
    }

    [Fact]
    public void Suggest_WhenSeveralAreTaken_KeepsCounting()
    {
        // Arrange and act.
        Existing("mindmap.adp");
        Existing("mindmap-2.adp");

        // Assert.
        Assert.Equal("mindmap-3", DiagramFileName.Suggest(new DiagramOrigin("freeplane", "mindmap"), _folder));
    }

    [Fact]
    public void Suggest_WhenAFolderHoldsTheName_TreatsItAsTakenToo()
    {
        // Act.
        Directory.CreateDirectory(IoPath.Combine(_folder, "mindmap.adp"));

        // Assert.
        Assert.Equal("mindmap-2", DiagramFileName.Suggest(new DiagramOrigin("freeplane", "mindmap"), _folder));
    }

    [Theory]
    [InlineData("da/ta", "da-ta")]
    [InlineData("a\\b", "a-b")]
    [InlineData("a:b", "a-b")]
    [InlineData("../escape", "escape")]
    public void Suggest_SanitisesATypeSegmentThatCouldNameAPath(string type, string expected)
    {
        // Act.
        // The vendor never reaches the name - only the type does - so that is where a segment
        // ADP does not control could otherwise smuggle a separator in.
        var suggestion = DiagramFileName.Suggest(new DiagramOrigin("vendor", type), _folder);

        // Assert.
        Assert.Equal(expected, suggestion);
        Assert.DoesNotContain(IoPath.DirectorySeparatorChar, suggestion);
        Assert.DoesNotContain(IoPath.AltDirectorySeparatorChar, suggestion);
    }

    [Theory]
    [InlineData("domain", "domain")]
    [InlineData("domain.adp", "domain")]
    [InlineData("domain.ADP", "domain")]
    [InlineData("domain.adp.adp", "domain.adp")]
    [InlineData("  domain.adp  ", "domain")]
    public void StripExtension_RemovesOneTrailingExtension(string typed, string expected)
    {
        // Arrange, act and assert.
        Assert.Equal(expected, DiagramFileName.StripExtension(typed));
    }

    [Theory]
    [InlineData("domain", "domain.adp")]
    [InlineData("domain.adp", "domain.adp")]
    [InlineData("domain.ADP", "domain.adp")]
    public void WithExtension_AlwaysYieldsExactlyOneExtension(string baseName, string expected)
    {
        // Arrange, act and assert.
        Assert.Equal(expected, DiagramFileName.WithExtension(baseName));
    }

    [Fact]
    public void WithExtension_AndStripExtension_AreEachOthersInverse()
    {
        // Arrange, act and assert.
        Assert.Equal("domain", DiagramFileName.StripExtension(DiagramFileName.WithExtension("domain")));
    }
}
