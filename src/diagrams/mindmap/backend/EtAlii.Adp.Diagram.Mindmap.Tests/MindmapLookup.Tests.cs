using EtAlii.Adp.Context;
using EtAlii.Adp.History;
using EtAlii.Adp.Specification.Fbl.Links;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.Mindmap.Tests;

/// <summary>
/// "Add from another document…" on a node (DISL 0.12 §9.4, etalii.adp spec 092, T021, FR-012, FR-013): the host's lookup
/// searches the workspace's linkable elements by name, and picking one adds a linked node for it as the node's last child, in
/// one undo step; nothing is added for a pick the workspace no longer holds.
/// </summary>
public sealed class MindmapLookupTests : IDisposable
{
    private const string Plain = "<map version=\"freeplane 1.11.5\">\n<node TEXT=\"Shop relaunch\" ID=\"ID_1\">\n<attribute NAME=\"adp-id\" VALUE=\"p4r8t1zc\"/>\n</node>\n</map>\n";

    private readonly TestFolder _folder = TestFolder.Create("adp-mindmap-lookup");
    private readonly ServiceProvider _services = new ServiceCollection().AddCommands().AddMindmapCommands().BuildServiceProvider();

    public void Dispose()
    {
        _services.Dispose();
        _folder.Dispose();
    }

    [Fact]
    public async Task TheLookup_OffersTheWorkspacesElementsByName_AndPickingOneAddsALinkedChild()
    {
        // Arrange.
        var path = Write();
        var actions = Actions();
        var offered = (await actions.DiscoverAsync(Target(path), TestContext.Current.CancellationToken)).ActionIds();

        // Act.
        var opened = await actions.ExecuteAsync(Target(path), MindmapContextActionProvider.AddLinkedActionId, TestContext.Current.CancellationToken);
        var lookup = Assert.IsType<ContextExecutionRequiresLookup>(opened).Request;
        var candidate = Assert.Single(lookup.Search("check"));
        var committed = await actions.CommitAsync(Target(path), MindmapContextActionProvider.AddLinkedActionId, candidate.Value, "", TestContext.Current.CancellationToken);

        // Assert.
        Assert.Contains(MindmapContextActionProvider.AddLinkedActionId, offered);
        Assert.Equal(("Checkout", "features.yaml", "specs"), (candidate.Name, candidate.DocumentName, candidate.Folder));
        Assert.True(committed.Completed, committed.Error);
        Assert.Matches("<node TEXT=\"Checkout\" ID=\"[^\"]+\">\n<attribute NAME=\"adp-link\" VALUE=\"\\$k7q2m9xa@features.yaml#checkout=Checkout\"/>\n</node>\n</node>\n</map>\n$", await ReadAsync(path));
    }

    [Fact]
    public async Task TheAddedNode_IsUndoneAsOneStep()
    {
        // Arrange.
        var path = Write();
        var actions = Actions();
        await actions.CommitAsync(Target(path), MindmapContextActionProvider.AddLinkedActionId, "$k7q2m9xa@features.yaml#checkout=Checkout", "", TestContext.Current.CancellationToken);

        // Act.
        await _services.GetRequiredService<IHistoryStackStore>().Get(ProjectRoot.From(_folder.Path)).UndoAsync(TestContext.Current.CancellationToken);

        // Assert.
        Assert.Equal(Plain, await ReadAsync(path));
    }

    [Fact]
    public async Task APickTheWorkspaceNoLongerHolds_AddsNothing()
    {
        // Arrange.
        var path = Write();

        // Act.
        var committed = await Actions().CommitAsync(Target(path), MindmapContextActionProvider.AddLinkedActionId, "$k7q2m9xa@features.yaml#gone=Gone", "", TestContext.Current.CancellationToken);

        // Assert.
        Assert.False(committed.Completed);
        Assert.Equal(Plain, await ReadAsync(path));
    }

    private MindmapContextActionProvider Actions() => new(
        _services.GetRequiredService<IHistoryStackStore>(), _services.GetRequiredService<IMindmapDocumentStore>(), new MindmapViewState(), new MindmapOpeningIndex(Workspace()));

    private ContextTarget Target(string path) => ContextTarget.ForElement(path, "ID_1", ProjectRoot.From(_folder.Path), ShortGuid.NewShortGuid());

    private string Write()
    {
        var path = IoPath.Combine(_folder.Path, "ideas.mm");
        File.WriteAllText(path, Plain);
        return path;
    }

    private static async Task<string> ReadAsync(string path) => (await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken)).ReplaceLineEndings("\n");

    private LinkWorkspace Workspace() => new(_folder.Path.Replace('\\', '/'), [
        new IndexedDocument(_folder.Path.Replace('\\', '/') + "/specs/features.yaml", "k7q2m9xa", "Name", [new IndexedElement("checkout", [new KeyValuePair<string, string>("Name", "Checkout")])], []),
    ]);
}
