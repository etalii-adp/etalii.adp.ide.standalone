using System.Text.RegularExpressions;
using EtAlii.Adp.Context;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Files;
using EtAlii.Adp.History;
using EtAlii.Adp.Specification.Fbl.Links;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.DependencyGraph.Tests;

/// <summary>
/// "Add from another document…" where the canvas was clicked (DISL 0.12 §9.4, etalii.adp spec 092, T021, FR-012, FR-013): the
/// host's lookup searches the workspace's linkable elements by name, and picking one adds a linked node for it there, writing its
/// link and no label, in one undo step; nothing is added for a pick the workspace no longer holds.
/// </summary>
public sealed class DependencyGraphLookupTests : IDisposable
{
    private const string Plain = "dependencies: 1\nid: s2v5b9hu\nelements:\n  - id: aaa\n    label: Web shop\n    x: 0\n    row: 0\n";

    private readonly TestFolder _folder = TestFolder.Create("adp-dgr-lookup");
    private readonly DependencyGraphDocumentStore _store = new(new FileWriter());
    private readonly IHistoryStackStore _historyStacks;
    private readonly ShortGuid _watchId = ShortGuid.NewShortGuid();

    public DependencyGraphLookupTests()
    {
        _historyStacks = TestHistory.For(s => { s.AddDependencyGraph(); s.AddSingleton<IDependencyGraphDocumentStore>(_store); });
    }

    public void Dispose() => _folder.Dispose();

    [Fact]
    public async Task TheLookup_OffersTheWorkspacesElementsByName_AndPickingOneAddsALinkedNodeWhereTheCanvasWasClicked()
    {
        // Arrange.
        var path = Write();
        var actions = Actions();
        var offered = (await actions.DiscoverAsync(Target(path, Placement), TestContext.Current.CancellationToken)).ActionIds();

        // Act.
        var opened = await actions.ExecuteAsync(Target(path, Placement), DependencyGraphContextActionProvider.AddLinkedActionId, TestContext.Current.CancellationToken);
        var lookup = Assert.IsType<ContextExecutionRequiresLookup>(opened).Request;
        var candidate = Assert.Single(lookup.Search("check"));
        var committed = await actions.CommitAsync(Target(path, Placement), DependencyGraphContextActionProvider.AddLinkedActionId, candidate.Value, "", TestContext.Current.CancellationToken);

        // Assert.
        Assert.Contains(DependencyGraphContextActionProvider.AddLinkedActionId, offered);
        Assert.Equal(("Checkout", "features.yaml", "specs"), (candidate.Name, candidate.DocumentName, candidate.Folder));
        Assert.True(committed.Completed, committed.Error);
        Assert.Matches("^" + Regex.Escape(Plain) + "  - id: [0-9a-z]{8}\\n    link: \"?\\$k7q2m9xa@features\\.yaml#checkout=Checkout\"?\\n    x: \"?240\"?\\n    row: \"?3\"?\\n$", await ReadAsync(path));
    }

    [Fact]
    public async Task TheAddedNode_IsUndoneAsOneStep()
    {
        // Arrange.
        var path = Write();
        await Actions().CommitAsync(Target(path, Placement), DependencyGraphContextActionProvider.AddLinkedActionId, "$k7q2m9xa@features.yaml#checkout=Checkout", "", TestContext.Current.CancellationToken);

        // Act.
        await _historyStacks.Get(ProjectRoot.From(_folder.Path)).UndoAsync(TestContext.Current.CancellationToken);

        // Assert.
        Assert.Equal(Plain, await ReadAsync(path));
    }

    [Fact]
    public async Task APickTheWorkspaceNoLongerHolds_AddsNothing()
    {
        // Arrange.
        var path = Write();

        // Act.
        var committed = await Actions().CommitAsync(Target(path, Placement), DependencyGraphContextActionProvider.AddLinkedActionId, "$k7q2m9xa@features.yaml#gone=Gone", "", TestContext.Current.CancellationToken);

        // Assert.
        Assert.False(committed.Completed);
        Assert.Equal(Plain, await ReadAsync(path));
    }

    private static string Placement => GestureIds.RowPlacement(240, 3);

    private DependencyGraphContextActionProvider Actions() => new(_historyStacks, _store, new DependencyGraphOpeningIndex(Workspace()));

    private ContextTarget Target(string path, string elementId) => ContextTarget.ForElement(path, elementId, ProjectRoot.From(_folder.Path), _watchId);

    private string Write()
    {
        var path = IoPath.Combine(_folder.Path, "services.dgr");
        File.WriteAllText(path, Plain);
        _store.Forget(path);
        return path;
    }

    private static async Task<string> ReadAsync(string path) => (await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken)).ReplaceLineEndings("\n");

    private LinkWorkspace Workspace() => new(_folder.Path.Replace('\\', '/'), [
        new IndexedDocument(_folder.Path.Replace('\\', '/') + "/specs/features.yaml", "k7q2m9xa", "Name", [new IndexedElement("checkout", [new KeyValuePair<string, string>("Name", "Checkout")])], []),
    ]);
}
