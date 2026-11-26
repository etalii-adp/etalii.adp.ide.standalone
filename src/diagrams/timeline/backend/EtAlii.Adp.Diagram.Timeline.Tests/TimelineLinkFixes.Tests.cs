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
/// The fixes of a linked element's finding on its menu (FBL 17.6, etalii.adp spec 092, T017): a broken or malformed link is
/// removed, leaving a plain element named by the name it cached, and an ambiguous one is resolved by choosing its document,
/// which gives the other candidates new document ids.
/// </summary>
public sealed class TimelineLinkFixesTests : IDisposable
{
    private readonly TestFolder _folder = TestFolder.Create("adp-timeline-link-fixes");
    private readonly TimelineDocumentStore _store = new(new FileWriter());
    private readonly IHistoryStackStore _historyStacks;

    public TimelineLinkFixesTests() =>
        _historyStacks = TestHistory.For(services => { services.AddTimeline(); services.AddSingleton<ITimelineDocumentStore>(_store); });

    public void Dispose() => _folder.Dispose();

    [Theory]
    [InlineData("$zzzzzzzz@missing.yaml#signin.Name=Sign-in")]
    [InlineData("$k7q2m9xa@features.yaml#gone.Name=Sign-in")]
    public async Task ABrokenLink_IsRemoved_LeavingAMomentNamedByItsCachedName(string link)
    {
        // Arrange.
        var path = Write(link);
        var actions = Actions(new TimelineOpeningIndex(Workspace()));

        // Act.
        var offered = (await actions.DiscoverAsync(Target(path), TestContext.Current.CancellationToken)).ActionIds();
        var result = await actions.ExecuteAsync(Target(path), DislLinkFixes.RemoveActionId, TestContext.Current.CancellationToken);

        // Assert.
        Assert.Contains(DislLinkFixes.RemoveActionId, offered);
        Assert.DoesNotContain(DislLinkFixes.ChooseActionId, offered);
        Assert.IsType<ContextExecutionCompleted>(result);
        Assert.Equal(Body("    label: Sign-in"), await ReadAsync(path));
    }

    [Fact]
    public async Task RemovingALink_IsUndone_AsOneStep()
    {
        // Arrange.
        var path = Write("$zzzzzzzz@missing.yaml#signin.Name=Sign-in");
        var actions = Actions(new TimelineOpeningIndex(Workspace()));
        await actions.ExecuteAsync(Target(path), DislLinkFixes.RemoveActionId, TestContext.Current.CancellationToken);

        // Act.
        await _historyStacks.Get(ProjectRoot.From(_folder.Path)).UndoAsync(TestContext.Current.CancellationToken);

        // Assert.
        Assert.Equal(Body("    link: $zzzzzzzz@missing.yaml#signin.Name=Sign-in"), await ReadAsync(path));
    }

    [Fact]
    public async Task AMalformedLink_IsRemovable_WithoutAnIndex()
    {
        // Arrange.
        var path = Write("features.yaml#signin");
        var actions = Actions(null);

        // Act.
        var offered = (await actions.DiscoverAsync(Target(path), TestContext.Current.CancellationToken)).ActionIds();

        // Assert.
        Assert.Contains(DislLinkFixes.RemoveActionId, offered);
    }

    [Fact]
    public async Task ALinkThatResolves_OffersNoFix()
    {
        // Arrange.
        var path = Write("$k7q2m9xa@features.yaml#signin.Name=Sign-in");
        var actions = Actions(new TimelineOpeningIndex(Workspace()));

        // Act.
        var offered = (await actions.DiscoverAsync(Target(path), TestContext.Current.CancellationToken)).ActionIds();
        var result = await actions.ExecuteAsync(Target(path), DislLinkFixes.RemoveActionId, TestContext.Current.CancellationToken);

        // Assert.
        Assert.DoesNotContain(DislLinkFixes.RemoveActionId, offered);
        Assert.DoesNotContain(DislLinkFixes.ChooseActionId, offered);
        Assert.IsType<ContextExecutionFailed>(result);
        Assert.Equal(Body("    link: $k7q2m9xa@features.yaml#signin.Name=Sign-in"), await ReadAsync(path));
    }

    [Fact]
    public async Task ChoosingACopy_GivesTheOtherCandidateANewId_AndLeavesTheLinkAsItIs()
    {
        // Arrange: a copy of the target, beside it in another folder, carries its id and name.
        const string link = "$k7q2m9xa@features.yaml#signin.Name=Sign-in";
        var path = Write(link);
        var index = new TimelineOpeningIndex(Workspace(("features.yaml", "k7q2m9xa"), ("copy/features.yaml", "k7q2m9xa")));
        var actions = Actions(index);

        // Act.
        var offered = (await actions.DiscoverAsync(Target(path), TestContext.Current.CancellationToken)).ActionIds();
        var choice = await actions.ExecuteAsync(Target(path), DislLinkFixes.ChooseActionId, TestContext.Current.CancellationToken);
        var committed = await actions.CommitAsync(Target(path), DislLinkFixes.ChooseActionId, Held("features.yaml"), "", TestContext.Current.CancellationToken);

        // Assert.
        Assert.Contains(DislLinkFixes.ChooseActionId, offered);
        Assert.DoesNotContain(DislLinkFixes.RemoveActionId, offered);
        var options = Assert.IsType<ContextExecutionRequiresChoice>(choice).Request.Options;
        Assert.Equal(["copy/features.yaml", "features.yaml"], options.Select(option => option.Label).Order(StringComparer.Ordinal));
        Assert.True(committed.Completed, committed.Error);
        Assert.Equal([Held("copy/features.yaml")], index.Renewed);
        Assert.Equal(Body($"    link: {link}"), await ReadAsync(path));
    }

    [Fact]
    public async Task ChoosingADocumentOfAnotherId_RewritesTheLinkToNameIt()
    {
        // Arrange: no document has the link's id, and two of other ids have its name.
        var path = Write("$zzzzzzzz@features.yaml#signin.Name=Sign-in");
        var index = new TimelineOpeningIndex(Workspace(("a/features.yaml", "a1a1a1a1"), ("b/features.yaml", "b2b2b2b2")));
        var actions = Actions(index);

        // Act.
        var committed = await actions.CommitAsync(Target(path), DislLinkFixes.ChooseActionId, Held("b/features.yaml"), "", TestContext.Current.CancellationToken);

        // Assert.
        Assert.True(committed.Completed, committed.Error);
        Assert.Equal([Held("a/features.yaml")], index.Renewed);
        Assert.Equal(Body("    link: $b2b2b2b2@features.yaml#signin.Name=Sign-in"), await ReadAsync(path));
    }

    [Fact]
    public async Task ChoosingADocumentThatIsNoCandidate_IsRefused_AndRenewsNothing()
    {
        // Arrange.
        var path = Write("$k7q2m9xa@features.yaml#signin.Name=Sign-in");
        var index = new TimelineOpeningIndex(Workspace(("features.yaml", "k7q2m9xa"), ("copy/features.yaml", "k7q2m9xa")));
        var actions = Actions(index);

        // Act.
        var committed = await actions.CommitAsync(Target(path), DislLinkFixes.ChooseActionId, Held("elsewhere.yaml"), "", TestContext.Current.CancellationToken);

        // Assert.
        Assert.False(committed.Completed);
        Assert.Empty(index.Renewed);
    }

    private TimelineContextActionProvider Actions(TimelineOpeningIndex? index) => new(_historyStacks, _store, index);

    private ContextTarget Target(string path) => ContextTarget.ForElement(path, "f-signin", ProjectRoot.From(_folder.Path), ShortGuid.NewShortGuid());

    private string Write(string link)
    {
        var path = IoPath.Combine(_folder.Path, "roadmap.tml");
        File.WriteAllText(path, Body($"    link: {link}"));
        _store.Forget(path);
        return path;
    }

    private static async Task<string> ReadAsync(string path) => (await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken)).ReplaceLineEndings("\n");

    private static string Body(string named) =>
        $"timeline: 1\nid: r6d3w8ne\nelements:\n  - id: f-signin\n{named}\n    begin: 2027-01-04\n    row: 1\n";

    private string Held(string relative) => _folder.Path.Replace('\\', '/') + "/" + relative;

    private LinkWorkspace Workspace(params (string Path, string Id)[] documents) => new(_folder.Path.Replace('\\', '/'),
        (documents.Length == 0 ? [("features.yaml", "k7q2m9xa")] : documents).Select(document => new IndexedDocument(
            Held(document.Path), document.Id, "Name", [new IndexedElement("signin", [new KeyValuePair<string, string>("Name", "Sign-in")])], [])));
}
