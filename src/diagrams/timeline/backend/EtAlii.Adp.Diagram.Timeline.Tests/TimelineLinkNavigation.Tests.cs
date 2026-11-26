using EtAlii.Adp.Context;
using EtAlii.Adp.Diagram.Modules;
using EtAlii.Adp.Files;
using EtAlii.Adp.History;
using EtAlii.Adp.Specification.Fbl.Links;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.Timeline.Tests;

/// <summary>
/// Going along a link from a timeline (DISL 4.12, FBL 17.5, etalii.adp spec 092, T022 and T023, FR-014, FR-015): a linked element
/// whose link resolves offers "Open linked element", which points at its target; an element that links point at offers "Linked
/// from (N)", which lists them and points at the one picked; neither is offered where it would lead nowhere.
/// </summary>
public sealed class TimelineLinkNavigationTests : IDisposable
{
    private const string Body = "timeline: 1\nid: r6d3w8ne\nelements:\n  - id: launch\n    label: Launch\n    begin: 2027-01-04\n    row: 0\n  - id: f-signin\n    link: $k7q2m9xa@features.yaml#signin=Sign-in\n    begin: 2027-01-11\n    row: 1\n  - id: f-gone\n    link: $k7q2m9xa@features.yaml#gone=Gone\n    begin: 2027-01-18\n    row: 2\n";

    private readonly TestFolder _folder = TestFolder.Create("adp-timeline-link-navigation");
    private readonly TimelineDocumentStore _store = new(new FileWriter());
    private readonly IHistoryStackStore _historyStacks;
    private readonly ShortGuid _watchId = ShortGuid.NewShortGuid();

    public TimelineLinkNavigationTests()
    {
        _historyStacks = TestHistory.For(s => { s.AddTimeline(); s.AddSingleton<ITimelineDocumentStore>(_store); });
    }

    public void Dispose() => _folder.Dispose();

    [Fact]
    public async Task ALinkedElementWhoseLinkResolves_OffersOpenLinkedElement_WhichPointsAtItsTarget()
    {
        // Arrange.
        var path = Write();
        var actions = Actions();

        // Act.
        var offered = (await actions.DiscoverAsync(Target(path, "f-signin"), TestContext.Current.CancellationToken)).ActionIds();
        var opened = await actions.ExecuteAsync(Target(path, "f-signin"), LinkNavigation.OpenActionId, TestContext.Current.CancellationToken);

        // Assert.
        Assert.Contains(LinkNavigation.OpenActionId, offered);
        var reveal = Assert.IsType<ContextExecutionReveals>(opened);
        Assert.Equal((IoPath.Combine(_folder.Path, "specs", "features.yaml"), "signin"), (reveal.FullPath, reveal.ElementId));
    }

    [Fact]
    public async Task ALinkThatDoesNotResolve_AndAnElementWithoutALink_OfferNeither()
    {
        // Arrange.
        var path = Write();
        var actions = Actions();

        // Act.
        var broken = (await actions.DiscoverAsync(Target(path, "f-gone"), TestContext.Current.CancellationToken)).ActionIds();
        var plain = (await actions.DiscoverAsync(Target(path, "launch"), TestContext.Current.CancellationToken)).ActionIds();

        // Assert.
        Assert.DoesNotContain(LinkNavigation.OpenActionId, broken);
        Assert.DoesNotContain(LinkNavigation.OpenActionId, plain);
        Assert.DoesNotContain(LinkNavigation.LinkedFromActionId, plain);
    }

    [Fact]
    public async Task AnElementLinksPointAt_OffersLinkedFrom_ListingThem_AndPickingOnePointsAtItsHolder()
    {
        // Arrange: a dependency graph links to the launch, twice, from two nodes.
        var path = Write();
        var actions = Actions(new IndexedDocument(Held("services.dgr"), "s2v5b9hu", "label", [new IndexedElement("pay", [new KeyValuePair<string, string>("label", "Payments")])], [
            new HeldLink("pay", "$r6d3w8ne@roadmap.tml#launch=Launch"),
            new HeldLink("ship", "$r6d3w8ne@roadmap.tml#launch=Launch"),
        ]));

        // Act.
        var offered = (await actions.DiscoverAsync(Target(path, "launch"), TestContext.Current.CancellationToken)).Single(group => group.Actions.Any(action => action.Id == LinkNavigation.LinkedFromActionId));
        var listed = await actions.ExecuteAsync(Target(path, "launch"), LinkNavigation.LinkedFromActionId, TestContext.Current.CancellationToken);
        var choice = Assert.IsType<ContextExecutionRequiresChoice>(listed).Request;
        var picked = await actions.CommitAsync(Target(path, "launch"), LinkNavigation.LinkedFromActionId, choice.Options[1].Id, "", TestContext.Current.CancellationToken);

        // Assert.
        Assert.Equal("Linked from (2)…", offered.Actions.Single(action => action.Id == LinkNavigation.LinkedFromActionId).Label);
        Assert.Equal(["Payments (services.dgr)", "ship (services.dgr)"], choice.Options.Select(option => option.Label));
        Assert.True(picked.Completed, picked.Error);
        var reveal = Assert.IsType<ContextExecutionReveals>(picked.Next);
        Assert.Equal((IoPath.Combine(_folder.Path, "services.dgr"), "ship"), (reveal.FullPath, reveal.ElementId));
    }

    private TimelineContextActionProvider Actions(params IndexedDocument[] more) => new(_historyStacks, _store, new TimelineOpeningIndex(new LinkWorkspace(Held(""), [
        new IndexedDocument(Held("specs/features.yaml"), "k7q2m9xa", "Name", [new IndexedElement("signin", [new KeyValuePair<string, string>("Name", "Sign-in")])], []),
        new IndexedDocument(Held("roadmap.tml"), "r6d3w8ne", "label", [new IndexedElement("launch", [new KeyValuePair<string, string>("label", "Launch")])], []),
        .. more,
    ])));

    private ContextTarget Target(string path, string elementId) => ContextTarget.ForElement(path, elementId, ProjectRoot.From(_folder.Path), _watchId);

    private string Held(string relative) => (_folder.Path.Replace('\\', '/') + "/" + relative).TrimEnd('/');

    private string Write()
    {
        var path = IoPath.Combine(_folder.Path, "roadmap.tml");
        File.WriteAllText(path, Body);
        _store.Forget(path);
        return path;
    }
}
