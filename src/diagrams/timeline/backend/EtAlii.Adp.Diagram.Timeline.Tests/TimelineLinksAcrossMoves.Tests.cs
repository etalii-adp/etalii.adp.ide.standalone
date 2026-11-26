using EtAlii.Adp.Files;
using EtAlii.Adp.Links;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.Timeline.Tests;

/// <summary>
/// Links survive what a person does to files in the hierarchy (etalii.adp spec 092, T019, SC-004, User Story 4): with the real
/// index over a real folder and its watcher, a target renamed and moved, and a consuming document copied, leave every link
/// resolving on the next opening, its document-name part refreshed, with no action by the person.
/// </summary>
public sealed class TimelineLinksAcrossMovesTests : IDisposable
{
    private const string Link = "$k7q2m9xa@milestones.tml#launch=Launch";

    private readonly TestFolder _folder = TestFolder.Create("timeline-links-across-moves");
    private readonly ProjectFileEvents _events = new();
    private readonly ServiceProvider _services;
    private readonly LinkIndex _index;

    public TimelineLinksAcrossMovesTests()
    {
        var services = new ServiceCollection();
        services.AddTimeline();
        _services = services.BuildServiceProvider();
        _index = new LinkIndex(_events, _services.GetServices<ILinkIndexer>(), new FileWriter());
    }

    public void Dispose()
    {
        _index.Dispose();
        _events.Dispose();
        _services.Dispose();
        _folder.Dispose();
    }

    [Fact]
    public async Task ATargetRenamedAndMoved_IsResolvedOnTheNextOpening_ByItsDocumentId()
    {
        // Arrange.
        var plan = Write("plan.tml", Consumer());
        Write("milestones.tml", Target());
        using var tracked = _index.Track(_folder.Path);

        // Act: renamed and moved into another folder in one go.
        Directory.CreateDirectory(_folder.Combine("archive"));
        File.Move(_folder.Combine("milestones.tml"), _folder.Combine("archive", "milestones-2027.tml"));
        Assert.True(await Eventually.TrueAsync(() => _index.WorkspaceOf(plan)?.Document(Held("archive/milestones-2027.tml")) is not null && _index.WorkspaceOf(plan)?.Document(Held("milestones.tml")) is null, TestContext.Current.CancellationToken));
        (bool linked, string label, string? pending) = await OpenAsync(plan);

        // Assert.
        Assert.True(linked);
        Assert.Equal("Launch", label);
        Assert.Contains("link: $k7q2m9xa@milestones-2027.tml#launch", pending, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ACopiedConsumer_ResolvesItsLinksAsTheOriginalDoes()
    {
        // Arrange.
        var plan = Write("plan.tml", Consumer());
        Write("milestones.tml", Target());
        using var tracked = _index.Track(_folder.Path);

        // Act.
        Directory.CreateDirectory(_folder.Combine("copy"));
        File.Copy(plan, _folder.Combine("copy", "plan.tml"));
        var copy = Held("copy/plan.tml");
        Assert.True(await Eventually.TrueAsync(() => _index.WorkspaceOf(copy)?.Document(copy) is not null, TestContext.Current.CancellationToken));

        // Assert.
        Assert.True((await OpenAsync(plan)).Linked);
        Assert.True((await OpenAsync(copy)).Linked);
    }

    /// <summary>Opens <paramref name="path"/> as a person would: through the store, which validates its links, and a session.</summary>
    private async Task<(bool Linked, string Label, string? Pending)> OpenAsync(string path)
    {
        var store = new TimelineDocumentStore(new FileWriter(), _index);
        var entry = await store.GetOrLoadAsync(path, TestContext.Current.CancellationToken);
        await using var session = new TimelineSession(path, store, new TimelineElementMapper(), links: _index);
        var add = Assert.IsType<DiagramAddDelta>(Assert.Single(await session.BaselineAsync(TestContext.Current.CancellationToken)));
        var payload = TimelineElementPayload.Parser.ParseFrom(add.Elements.Single(element => element.Id == "m-launch").Payload.Span);
        return (payload.Linked, payload.Label, entry.Pending);
    }

    private static string Consumer() => $"timeline: 1\nid: p4l8n2ma\nelements:\n  - id: m-launch\n    link: {Link}\n    begin: 2027-06-01\n    row: 0\n";

    private static string Target() => "timeline: 1\nid: k7q2m9xa\nelements:\n  - id: launch\n    label: Launch\n    begin: 2027-06-01\n    row: 0\n";

    private string Write(string relative, string text)
    {
        var path = _folder.Combine(relative);
        File.WriteAllText(path, text);
        return Held(relative);
    }

    private string Held(string relative) => IoPath.GetFullPath(_folder.Combine(relative)).Replace('\\', '/');
}
