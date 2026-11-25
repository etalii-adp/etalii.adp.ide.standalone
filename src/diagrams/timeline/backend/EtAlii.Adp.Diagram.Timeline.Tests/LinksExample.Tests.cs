using EtAlii.Adp.Links;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.Timeline.Tests;

/// <summary>
/// Spec 089's worked workspace (<c>Fixtures/links-example/</c>) opens through the served binding (etalii.adp spec 092,
/// T011, SC-001): its document id and every linked element are read, each named by its link, and nothing is reported.
/// </summary>
public class LinksExampleTests
{
    [Fact]
    public async Task TheRoadmap_ReadsItsDocumentIdAndEveryLinkedElement_WithNoFinding()
    {
        // Arrange.
        var text = await File.ReadAllTextAsync(IoPath.Combine(AppContext.BaseDirectory, "Fixtures", "links-example", "roadmap.tml"), TestContext.Current.CancellationToken);

        // Act.
        var body = TimelineBody.Parse(text);
        var model = TimelineReading.Of(text);

        // Assert.
        Assert.Equal("r6d3w8ne", body.Model.DocumentId);
        Assert.Empty(body.Model.Findings);
        Assert.Equal(
            ["Sign-in", "Checkout", "Beta", "Ask the support team", "Payment gateway"],
            model.Elements.Select(element => element.Label));
        Assert.Equal(4, model.Elements.Count(element => element.Link is not null));
        Assert.Equal(2, model.Connections.Count);
        Assert.Equal(text, body.Text);
    }

    [Fact]
    public async Task TheRoadmap_IsIndexed_ByItsDocumentIdAndTheLinksItsElementsHold()
    {
        // Arrange: the link index's reader the module registers (spec 092, T014).
        var services = new ServiceCollection();
        services.AddTimeline();
        var indexer = Assert.Single(services.BuildServiceProvider().GetServices<ILinkIndexer>());
        var path = IoPath.Combine(AppContext.BaseDirectory, "Fixtures", "links-example", "roadmap.tml");
        var bytes = await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken);

        // Act.
        var indexed = indexer.Index(path, bytes);

        // Assert.
        Assert.True(indexer.Claims(path));
        Assert.False(indexer.Claims(IoPath.ChangeExtension(path, ".txt")));
        Assert.NotNull(indexed);
        Assert.Equal("r6d3w8ne", indexed.DocumentId);
        Assert.Equal(4, indexed.Links.Count);
        Assert.All(indexed.Links, link => Assert.StartsWith("$", link.Text, StringComparison.Ordinal));
        Assert.NotEmpty(indexed.Elements);
    }
}
