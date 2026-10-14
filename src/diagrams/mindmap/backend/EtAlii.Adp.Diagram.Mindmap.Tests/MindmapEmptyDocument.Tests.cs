using Xunit;

namespace EtAlii.Adp.Diagram.Mindmap.Tests;

public class MindmapEmptyDocumentTests
{
    [Fact]
    public void CreateEmptyDocument_HasOneRootNamedAfterTheFile_AndNothingElse()
    {
        // Act.
        var document = MindmapDocument.Parse(MindmapDocument.EmptyDocument("domain"));

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
        var text = MindmapDocument.EmptyDocument("domain");

        // Assert.
        Assert.Equal(text, MindmapDocument.Parse(text, assignMissingIds: false).ToText());
    }

    [Fact]
    public void CreateEmptyDocument_EscapesTheName()
    {
        // Act.
        var text = MindmapDocument.EmptyDocument("a & \"b\"");

        // Assert.
        Assert.Contains("TEXT=\"a &amp; &quot;b&quot;\"", text, StringComparison.Ordinal);
        Assert.Equal("a & \"b\"", MindmapDocument.Parse(text).Root.Text);
    }

    [Fact]
    public void Definition_DeclaresTheMmExtension()
    {
        // Arrange, act and assert.
        Assert.Equal(".mm", Diagram.Mindmap.Extension);
        Assert.True(Diagram.Mindmap.HasDocumentSibling);
    }
}
