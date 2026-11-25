using EtAlii.Adp.Context;
using EtAlii.Adp.Diagram.Modules;
using EtAlii.Adp.History;
using EtAlii.Adp.Specification.Fbl.Links;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.Mindmap.Tests;

/// <summary>
/// The fixes of a linked node's finding on its menu (FBL 17.6, <c>mindmap.md</c> 3.8, etalii.adp spec 092, T017): a broken
/// link's <c>adp-link</c> is removed, leaving an ordinary node with the text the link cached, and an ambiguous one is resolved
/// by choosing its document.
/// </summary>
public sealed class MindmapLinkFixesTests : IDisposable
{
    private readonly TestFolder _folder = TestFolder.Create("adp-mindmap-link-fixes");
    private readonly ServiceProvider _services = new ServiceCollection().AddCommands().AddMindmapCommands().BuildServiceProvider();

    public void Dispose()
    {
        _services.Dispose();
        _folder.Dispose();
    }

    [Fact]
    public async Task ABrokenLink_IsRemoved_LeavingTheTextItCached_AndIsUndoneAsOneStep()
    {
        // Arrange.
        var path = Write("$k7q2m9xa@features.yaml#gone.Name=Checkout");
        var actions = Actions(new MindmapOpeningIndex(Workspace()));

        // Act.
        var offered = (await actions.DiscoverAsync(Target(path), TestContext.Current.CancellationToken)).ActionIds();
        var result = await actions.ExecuteAsync(Target(path), DislLinkFixes.RemoveActionId, TestContext.Current.CancellationToken);
        var removed = await ReadAsync(path);
        await History.UndoAsync(TestContext.Current.CancellationToken);

        // Assert.
        Assert.Contains(DislLinkFixes.RemoveActionId, offered);
        Assert.IsType<ContextExecutionCompleted>(result);
        Assert.Equal(Map("<node TEXT=\"Checkout\" ID=\"ID_2\"/>"), removed);
        Assert.Equal(Map(Linked("$k7q2m9xa@features.yaml#gone.Name=Checkout")), await ReadAsync(path));
    }

    [Fact]
    public async Task ALinkThatResolves_OffersNoFix()
    {
        // Arrange.
        var path = Write("$k7q2m9xa@features.yaml#checkout.Name=Checkout");
        var actions = Actions(new MindmapOpeningIndex(Workspace()));

        // Act.
        var offered = (await actions.DiscoverAsync(Target(path), TestContext.Current.CancellationToken)).ActionIds();

        // Assert.
        Assert.DoesNotContain(DislLinkFixes.RemoveActionId, offered);
        Assert.DoesNotContain(DislLinkFixes.ChooseActionId, offered);
    }

    [Fact]
    public async Task ChoosingADocumentOfAnotherId_RenewsTheOther_AndRewritesTheLinkToNameIt()
    {
        // Arrange: no document has the link's id, and two of other ids have its name.
        var path = Write("$zzzzzzzz@features.yaml#checkout.Name=Checkout");
        var index = new MindmapOpeningIndex(Workspace(("a/features.yaml", "a1a1a1a1"), ("b/features.yaml", "b2b2b2b2")));
        var actions = Actions(index);

        // Act.
        var offered = (await actions.DiscoverAsync(Target(path), TestContext.Current.CancellationToken)).ActionIds();
        var committed = await actions.CommitAsync(Target(path), DislLinkFixes.ChooseActionId, Held("b/features.yaml"), "", TestContext.Current.CancellationToken);

        // Assert.
        Assert.Contains(DislLinkFixes.ChooseActionId, offered);
        Assert.True(committed.Completed, committed.Error);
        Assert.Equal([Held("a/features.yaml")], index.Renewed);
        Assert.Equal(Map(Linked("$b2b2b2b2@features.yaml#checkout.Name=Checkout")), await ReadAsync(path));
    }

    private IHistoryStack History => _services.GetRequiredService<IHistoryStackStore>().Get(ProjectRoot.From(_folder.Path));

    private MindmapContextActionProvider Actions(MindmapOpeningIndex index) => new(
        _services.GetRequiredService<IHistoryStackStore>(), _services.GetRequiredService<IMindmapDocumentStore>(), new MindmapViewState(), index);

    private ContextTarget Target(string path) => ContextTarget.ForElement(path, "ID_2", ProjectRoot.From(_folder.Path), ShortGuid.NewShortGuid());

    private string Write(string link)
    {
        var path = IoPath.Combine(_folder.Path, "ideas.mm");
        File.WriteAllText(path, Map(Linked(link)));
        return path;
    }

    private static async Task<string> ReadAsync(string path) => (await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken)).ReplaceLineEndings("\n");

    private static string Linked(string link) => $"<node TEXT=\"Checkout\" ID=\"ID_2\">\n<attribute NAME=\"adp-link\" VALUE=\"{link}\"/>\n</node>";

    private static string Map(string node) =>
        $"<map version=\"freeplane 1.11.5\">\n<node TEXT=\"Shop relaunch\" ID=\"ID_1\">\n<attribute NAME=\"adp-id\" VALUE=\"p4r8t1zc\"/>\n{node}\n</node>\n</map>\n";

    private string Held(string relative) => _folder.Path.Replace('\\', '/') + "/" + relative;

    private LinkWorkspace Workspace(params (string Path, string Id)[] documents) => new(_folder.Path.Replace('\\', '/'),
        (documents.Length == 0 ? [("features.yaml", "k7q2m9xa")] : documents).Select(document => new IndexedDocument(
            Held(document.Path), document.Id, "Name", [new IndexedElement("checkout", [new KeyValuePair<string, string>("Name", "Checkout")])], [])));
}
