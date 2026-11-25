using System.Text;
using EtAlii.Adp.Specification.Fbl.Plugins;
using Xunit;

namespace EtAlii.Adp.Diagram.Mindmap.Tests;

/// <summary>
/// Spec 089's worked workspace (<c>Fixtures/links-example/</c>) opens through the plugin at 0.2.0 (etalii.adp spec 092,
/// T009, T011, SC-001): its document id and every linked node are read, and the definition's rules report nothing.
/// </summary>
public class LinksExampleTests
{
    [Fact]
    public async Task TheIdeas_ReadTheirDocumentIdAndEveryLinkedNode_WithNoFinding()
    {
        // Arrange.
        var text = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "links-example", "ideas.mm"), TestContext.Current.CancellationToken);

        // Act.
        var read = new MindmapFreeplanePlugin().Read(new PluginReadRequest([new PluginFile("", Encoding.UTF8.GetBytes(text))]));
        var diagram = MindmapDefinition.ModelOf(MindmapDocument.Parse(text, assignMissingIds: false));

        // Assert.
        Assert.False(read.Unreadable);
        Assert.Equal("p4r8t1zc", read.Elements[0].Attributes["documentId"]);
        Assert.Equal(3, diagram.NodesOfType("LinkedNode").Count);
        Assert.All(diagram.NodesOfType("LinkedNode"), node => Assert.StartsWith("$", node.ValueOf("linkedElement") as string, StringComparison.Ordinal));
        Assert.Empty(MindmapDefinition.Findings(text, "ideas.mm"));
    }
}
