using Xunit;

namespace EtAlii.Adp.Diagram.Mindmap.Tests;

public class MindmapDocumentFactoryTests
{
    private readonly MindmapDocumentFactory _factory = new();

    [Fact]
    public void Origin_IsTheModulesOwn()
    {
        // Arrange, act and assert.
        Assert.Equal(Diagram.Definition.Origin, _factory.Origin);
    }

    [Fact]
    public void CreateEmptyDocument_HasOneRootNamedAfterTheFile_AndNothingElse()
    {
        // Act.
        var document = MindmapDocument.Parse(_factory.CreateEmptyDocument("domain"));

        // Assert.
        Assert.Equal("domain", document.Root.Text);
        Assert.Empty(document.Root.Children);
        Assert.Single(document.Nodes);
        Assert.StartsWith("ID_", document.Root.Id, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateEmptyDocument_RoundTripsByteForByte()
    {
        // Act.
        var text = _factory.CreateEmptyDocument("domain");

        // Assert.
        Assert.Equal(text, MindmapDocument.Parse(text, assignMissingIds: false).ToText());
    }

    [Fact]
    public void CreateEmptyDocument_EscapesTheName()
    {
        // Act.
        var text = _factory.CreateEmptyDocument("a & \"b\"");

        // Assert.
        Assert.Contains("TEXT=\"a &amp; &quot;b&quot;\"", text, StringComparison.Ordinal);
        Assert.Equal("a & \"b\"", MindmapDocument.Parse(text).Root.Text);
    }

    [Fact]
    public void Definition_DeclaresTheMmExtension()
    {
        // Arrange, act and assert.
        Assert.Equal(".mm", Diagram.Definition.Extension);
        Assert.True(Diagram.Definition.HasDocumentSibling);
    }
}
