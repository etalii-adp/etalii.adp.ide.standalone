using System.Text;
using EtAlii.Adp.Links;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EtAlii.Adp.Diagram.Mindmap.Tests;

/// <summary>
/// What a knowledge relation's "Create" adds to a mind map (etalii.adp spec 092, FR-025): the index's reader for maps offers
/// a node, and adds one named as typed as the last child of the central topic, refusing an id the map holds already.
/// </summary>
public sealed class MindmapElementCreationTests
{
    private const string Plain = "<map version=\"freeplane 1.11.5\">\n<node TEXT=\"Shop relaunch\" ID=\"ID_1\">\n<attribute NAME=\"adp-id\" VALUE=\"p4r8t1zc\"/>\n</node>\n</map>\n";

    private static ILinkIndexer Indexer()
    {
        var services = new ServiceCollection();
        services.AddMindmap(new ConfigurationBuilder().Build());
        return services.BuildServiceProvider().GetServices<ILinkIndexer>().Single(indexer => indexer.Claims("relaunch.mm"));
    }

    [Fact]
    public void TheIndexer_OffersANode_AndANewIdOfTheTypesShape()
    {
        // Arrange.
        var indexer = Indexer();

        // Act.
        var kinds = indexer.CreatableKinds.Select(kind => kind.Id);
        var id = indexer.NewElementId();

        // Assert.
        Assert.Equal(["Node"], kinds);
        Assert.False(string.IsNullOrEmpty(id));
    }

    [Fact]
    public void ANode_IsAddedAsAChildOfTheCentralTopic_NamedAsTyped()
    {
        // Arrange.
        var indexer = Indexer();

        // Act.
        var grown = indexer.WithElement(Encoding.UTF8.GetBytes(Plain), "Node", "n3w0e1em", "Payments");

        // Assert.
        Assert.NotNull(grown);
        var text = Encoding.UTF8.GetString(grown);
        Assert.Contains("TEXT=\"Shop relaunch\"", text, StringComparison.Ordinal);
        var element = Assert.Single(indexer.Index("relaunch.mm", grown)?.Elements ?? [], candidate => candidate.Id == "n3w0e1em");
        Assert.Contains(element.Attributes, attribute => attribute.Value == "Payments");
    }

    [Fact]
    public void AnIdTheMapHoldsAlready_AddsNothing()
    {
        // Act.
        var grown = Indexer().WithElement(Encoding.UTF8.GetBytes(Plain), "Node", "ID_1", "Payments");

        // Assert.
        Assert.Null(grown);
    }
}
