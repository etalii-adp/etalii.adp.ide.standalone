using EtAlii.Adp.Links;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EtAlii.Adp.Diagram.DependencyGraph.Tests;

/// <summary>
/// Spec 089's worked workspace (<c>Fixtures/links-example/</c>) opens through the served binding (etalii.adp spec 092,
/// T011, SC-001): its document id and every linked node are read, and the definition's rules report nothing.
/// </summary>
public class LinksExampleTests
{
    [Fact]
    public async Task TheServices_ReadItsDocumentIdAndEveryLinkedNode_WithNoFinding()
    {
        // Arrange.
        var text = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "links-example", "services.dgr"), TestContext.Current.CancellationToken);

        // Act.
        var body = DependencyGraphBody.Parse(text);

        // Assert.
        Assert.Equal("s2v5b9hu", body.Model.DocumentId);
        Assert.Empty(body.Model.Findings);
        Assert.Empty(DependencyGraphFindings.Judge(body));
        Assert.Equal(3, body.Disl.Diagram.Elements.Count(element => element.Type.Name == "LinkedNode"));
        Assert.Equal(2, body.Disl.Diagram.Relations.Count);
    }

    [Fact]
    public async Task TheServices_IsIndexed_ByItsDocumentIdAndTheLinksItsElementsHold()
    {
        // Arrange: the link index's reader the module registers (spec 092, T014).
        var services = new ServiceCollection();
        services.AddDependencyGraph();
        var indexer = Assert.Single(services.BuildServiceProvider().GetServices<ILinkIndexer>());
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "links-example", "services.dgr");
        var bytes = await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken);

        // Act.
        var indexed = indexer.Index(path, bytes);

        // Assert.
        Assert.True(indexer.Claims(path));
        Assert.False(indexer.Claims(Path.ChangeExtension(path, ".txt")));
        Assert.NotNull(indexed);
        Assert.Equal("s2v5b9hu", indexed.DocumentId);
        Assert.Equal(3, indexed.Links.Count);
        Assert.All(indexed.Links, link => Assert.StartsWith("$", link.Text, StringComparison.Ordinal));
        Assert.NotEmpty(indexed.Elements);
    }
}
