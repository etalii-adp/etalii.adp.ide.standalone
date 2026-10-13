using Xunit;

namespace EtAlii.Adp.Editor.Xml.Tests;

public class EditorTests
{
    /// <summary>
    /// This module declares exactly one editor, so Single() is the assertion as well as the
    /// accessor: if it ever grows a second, these tests fail rather than silently checking
    /// whichever one happened to be first.
    /// </summary>
    private static EditorDefinition Definition => Assert.Single(Editor.Definitions);

    [Fact]
    public void Definition_ClaimsTheXmlExtensions_AndIsNoFallback()
    {
        // Arrange, act and assert.
        Assert.Equal("xml", Definition.Id);
        Assert.Equal(new[] { ".xml", ".xsd", ".xsl", ".xslt" }, Definition.Extensions);
        Assert.Equal("XML editor", Definition.Title);
        Assert.False(Definition.IsFallback);
        Assert.NotNull(Definition.Build);
    }

    [Fact]
    public void NoEdslSpecification_IsWrittenOrRead()
    {
        // Arrange: the module's folder, every file in it.
        var module = RepositoryPaths.Under("src", "editors", "xml");

        // Act.
        var written = Directory.EnumerateFiles(module, "*.eds", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .ToArray();
        var read = Directory.EnumerateFiles(Path.Combine(module, "backend", "EtAlii.Adp.Editor.Xml"), "*.cs")
            .Concat(Directory.EnumerateFiles(Path.Combine(module, "client"), "*.ts*"))
            .Where(path => File.ReadAllText(path).Contains(".eds", StringComparison.Ordinal))
            .ToArray();

        // Assert: xml-editor Requirement 10.3 - EDSL has no content to write one in, and the
        // module is code until it has; what a specification would state is in the readme.
        Assert.Empty(written);
        Assert.Empty(read);
    }
}
