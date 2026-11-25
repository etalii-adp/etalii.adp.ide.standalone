using EtAlii.Adp.Context;
using EtAlii.Adp.Diagram.Modules;
using EtAlii.Adp.Files;
using EtAlii.Adp.History;
using EtAlii.Adp.Specification.Fbl.Links;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.DependencyGraph.Tests;

/// <summary>
/// The fixes of a linked node's finding on its menu (FBL 17.6, etalii.adp spec 092, T017): a broken link is removed, leaving
/// an ordinary node named by the name it cached, and an ambiguous one is resolved by choosing its document.
/// </summary>
public sealed class DependencyGraphLinkFixesTests : IDisposable
{
    private readonly TestFolder _folder = TestFolder.Create("adp-dgr-link-fixes");
    private readonly DependencyGraphDocumentStore _store = new(new FileWriter());
    private readonly IHistoryStackStore _historyStacks;

    public DependencyGraphLinkFixesTests() =>
        _historyStacks = TestHistory.For(services => { services.AddDependencyGraph(); services.AddSingleton<IDependencyGraphDocumentStore>(_store); });

    public void Dispose() => _folder.Dispose();

    [Fact]
    public async Task ABrokenLink_IsRemoved_LeavingANodeNamedByItsCachedName_AndIsUndoneAsOneStep()
    {
        // Arrange.
        var path = Write("$zzzzzzzz@missing.yaml#checkout.Name=Checkout");
        var actions = Actions(new DependencyGraphOpeningIndex(Workspace()));

        // Act.
        var offered = (await actions.DiscoverAsync(Target(path), TestContext.Current.CancellationToken)).ActionIds();
        var result = await actions.ExecuteAsync(Target(path), DislLinkFixes.RemoveActionId, TestContext.Current.CancellationToken);
        var removed = await ReadAsync(path);
        await _historyStacks.Get(ProjectRoot.From(_folder.Path)).UndoAsync(TestContext.Current.CancellationToken);

        // Assert.
        Assert.Contains(DislLinkFixes.RemoveActionId, offered);
        Assert.IsType<ContextExecutionCompleted>(result);
        Assert.Equal(Body("    label: Checkout"), removed);
        Assert.Equal(Body("    link: $zzzzzzzz@missing.yaml#checkout.Name=Checkout"), await ReadAsync(path));
    }

    [Fact]
    public async Task ChoosingADocumentOfAnotherId_RenewsTheOther_AndRewritesTheLinkToNameIt()
    {
        // Arrange: no document has the link's id, and two of other ids have its name.
        var path = Write("$zzzzzzzz@features.yaml#checkout.Name=Checkout");
        var index = new DependencyGraphOpeningIndex(Workspace(("a/features.yaml", "a1a1a1a1"), ("b/features.yaml", "b2b2b2b2")));
        var actions = Actions(index);

        // Act.
        var offered = (await actions.DiscoverAsync(Target(path), TestContext.Current.CancellationToken)).ActionIds();
        var committed = await actions.CommitAsync(Target(path), DislLinkFixes.ChooseActionId, Held("b/features.yaml"), "", TestContext.Current.CancellationToken);

        // Assert.
        Assert.Contains(DislLinkFixes.ChooseActionId, offered);
        Assert.True(committed.Completed, committed.Error);
        Assert.Equal([Held("a/features.yaml")], index.Renewed);
        Assert.Equal(Body("    link: $b2b2b2b2@features.yaml#checkout.Name=Checkout"), await ReadAsync(path));
    }

    private DependencyGraphContextActionProvider Actions(DependencyGraphOpeningIndex index) => new(_historyStacks, _store, index);

    private ContextTarget Target(string path) => ContextTarget.ForElement(path, "pay", ProjectRoot.From(_folder.Path), ShortGuid.NewShortGuid());

    private string Write(string link)
    {
        var path = IoPath.Combine(_folder.Path, "services.dgr");
        File.WriteAllText(path, Body($"    link: {link}"));
        _store.Forget(path);
        return path;
    }

    private static async Task<string> ReadAsync(string path) => (await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken)).ReplaceLineEndings("\n");

    private static string Body(string named) => $"dependencies: 1\nid: s2v5b9hu\nelements:\n  - id: pay\n{named}\n    x: 0\n    row: 2\n";

    private string Held(string relative) => _folder.Path.Replace('\\', '/') + "/" + relative;

    private LinkWorkspace Workspace(params (string Path, string Id)[] documents) => new(_folder.Path.Replace('\\', '/'),
        (documents.Length == 0 ? [("features.yaml", "k7q2m9xa")] : documents).Select(document => new IndexedDocument(
            Held(document.Path), document.Id, "Name", [new IndexedElement("checkout", [new KeyValuePair<string, string>("Name", "Checkout")])], [])));
}
