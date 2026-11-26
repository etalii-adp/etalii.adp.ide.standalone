using System.Text;
using EtAlii.Adp.Links;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EtAlii.Adp.Diagram.Timeline.Tests;

/// <summary>
/// What a knowledge relation's "Create" adds to a timeline (etalii.adp spec 092, FR-025): the index's reader for timelines
/// offers a moment and a period, and adds the one asked for, named as typed, beginning today on the first row, with the rest
/// of the file as it was.
/// </summary>
public sealed class TimelineElementCreationTests
{
    private const string Plain = "timeline: 1\nid: r6d3w8ne\nelements:\n  - id: aaa\n    label: Launch\n    begin: 2027-01-04\n    row: 0\n";

    private static ILinkIndexer Indexer()
    {
        var services = new ServiceCollection();
        services.AddTimeline();
        return services.BuildServiceProvider().GetServices<ILinkIndexer>().Single(indexer => indexer.Claims("plan.tml"));
    }

    [Fact]
    public void TheIndexer_OffersAMomentAndAPeriod_AndANewIdOfTheTypesShape()
    {
        // Arrange.
        var indexer = Indexer();

        // Act.
        var kinds = indexer.CreatableKinds.Select(kind => kind.Id);
        var id = indexer.NewElementId();

        // Assert.
        Assert.Equal(["Moment", "Period"], kinds);
        Assert.Matches("^[0-9a-z]{8}$", id);
    }

    [Theory]
    [InlineData("Moment", false)]
    [InlineData("Period", true)]
    public void AnElementAsked_IsAddedTodayOnTheFirstRow_AndTheRestOfTheFileIsKept(string kind, bool ends)
    {
        // Arrange.
        var indexer = Indexer();
        var today = TimelineScale.ToText(DateTimeOffset.UtcNow, TimelinePrecision.Date);

        // Act.
        var grown = indexer.WithElement("plan.tml", Encoding.UTF8.GetBytes(Plain), kind, "n3w0e1em", "Retro");

        // Assert.
        Assert.NotNull(grown);
        var text = Encoding.UTF8.GetString(grown);
        Assert.StartsWith(Plain, text, StringComparison.Ordinal);
        var element = Assert.Single(indexer.Index("plan.tml", grown)?.Elements ?? [], candidate => candidate.Id == "n3w0e1em");
        var attributes = element.Attributes.ToDictionary(attribute => attribute.Key, attribute => attribute.Value);
        Assert.Equal("Retro", attributes["label"]);
        Assert.Contains($"begin: {today}", text, StringComparison.Ordinal);
        Assert.Equal(ends, text.Contains("end:", StringComparison.Ordinal));
    }

    [Fact]
    public void AKindTheTimelineDoesNotOffer_AddsNothing()
    {
        // Act.
        var grown = Indexer().WithElement("plan.tml", Encoding.UTF8.GetBytes(Plain), "Node", "n3w0e1em", "Retro");

        // Assert.
        Assert.Null(grown);
    }
}
