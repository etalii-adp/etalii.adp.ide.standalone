using System.Text;
using EtAlii.Adp.Links;
using EtAlii.Adp.Specification.Fbl.Plugins;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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

    [Fact]
    public async Task TheIdeas_IsIndexed_ByItsDocumentIdAndTheLinksItsElementsHold()
    {
        // Arrange: the link index's reader the module registers (spec 092, T014).
        var services = new ServiceCollection();
        services.AddMindmap(new ConfigurationBuilder().Build());
        var indexer = Assert.Single(services.BuildServiceProvider().GetServices<ILinkIndexer>());
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "links-example", "ideas.mm");
        var bytes = await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken);

        // Act.
        var indexed = indexer.Index(path, bytes);

        // Assert.
        Assert.True(indexer.Claims(path));
        Assert.False(indexer.Claims(Path.ChangeExtension(path, ".txt")));
        Assert.NotNull(indexed);
        Assert.Equal("p4r8t1zc", indexed.DocumentId);
        Assert.Equal(3, indexed.Links.Count);
        Assert.All(indexed.Links, link => Assert.StartsWith("$", link.Text, StringComparison.Ordinal));
        Assert.NotEmpty(indexed.Elements);
    }
}
