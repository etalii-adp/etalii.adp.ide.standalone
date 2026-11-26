using System.Text;
using System.Text.RegularExpressions;
using EtAlii.Adp.Links;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EtAlii.Adp.Diagram.DependencyGraph.Tests;

/// <summary>
/// What a knowledge relation's "Create" adds to a dependency graph (etalii.adp spec 092, FR-025): the index's reader for
/// dependency graphs offers a node, and adds one named as typed on a row of its own below the others, with the rest of the
/// file as it was.
/// </summary>
public sealed class DependencyGraphElementCreationTests
{
    private const string Plain = "dependencies: 1\nid: s2v5b9hu\nelements:\n  - id: aaa\n    label: Web shop\n    x: 0\n    row: 0\n";

    private static ILinkIndexer Indexer()
    {
        var services = new ServiceCollection();
        services.AddDependencyGraph();
        return services.BuildServiceProvider().GetServices<ILinkIndexer>().Single(indexer => indexer.Claims("shop.dgr"));
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
    public void ANode_IsAddedOnTheRowBelowTheLast_AndTheRestOfTheFileIsKept()
    {
        // Arrange.
        var indexer = Indexer();

        // Act.
        var grown = indexer.WithElement(Encoding.UTF8.GetBytes(Plain), "Node", "n3w0e1em", "Payments");

        // Assert.
        Assert.NotNull(grown);
        var element = Assert.Single(indexer.Index("shop.dgr", grown)?.Elements ?? [], candidate => candidate.Id == "n3w0e1em");
        Assert.Contains(element.Attributes, attribute => attribute is { Key: "label", Value: "Payments" });
        Assert.Matches("^" + Regex.Escape(Plain) + "  - id: n3w0e1em\\n    label: Payments\\n    x: \"?0\"?\\n    row: \"?1\"?\\n$", Encoding.UTF8.GetString(grown));
    }

    [Fact]
    public void AKindTheGraphDoesNotOffer_AddsNothing()
    {
        // Act.
        var grown = Indexer().WithElement(Encoding.UTF8.GetBytes(Plain), "Period", "n3w0e1em", "Payments");

        // Assert.
        Assert.Null(grown);
    }
}
