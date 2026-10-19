using EtAlii.Adp.Files;
using EtAlii.Adp.Specification.Disl;
using EtAlii.Adp.Specification.Fbl.Planning;
using Xunit;

namespace EtAlii.Adp.Diagram.DependencyGraph.Tests;

/// <summary>The store over dependency graphs, held to the contract every module's is, and what is its own.</summary>
public sealed class DependencyGraphDocumentStoreTests : DocumentStoreContract<IDependencyGraphDocumentStore, DependencyGraphBody, DislModel>
{
    internal static string Simple => Path.Combine(AppContext.BaseDirectory, "Fixtures", "simple.dgr");

    protected override string Example => Simple;

    protected override string EditMarker => "Store contract marker";

    protected override IDependencyGraphDocumentStore CreateStore() => new DependencyGraphDocumentStore(new FileWriter());

    protected override void Edit(DependencyGraphBody document) =>
        document.Change(new ModelChange.Set(document.Model.Elements.First(element => element.Type == "Node").Id, new Dictionary<string, object?> { ["label"] = EditMarker }));

    protected override string TextOf(DependencyGraphBody document) => document.Text;

    protected override bool IsEmpty(DislModel model) => model.Diagram.Elements.Count == 0;

    [Fact]
    public async Task AGraphThatIsNotYaml_IsHeldUnreadable_AndASaveIsRefusedUnwritten()
    {
        // Arrange.
        using var folder = TestFolder.Create("dependency-graph-store");
        var body = folder.Combine("broken.dgr");
        await File.WriteAllTextAsync(body, "elements: [\n  - id: a\n", TestContext.Current.CancellationToken);
        var before = await File.ReadAllTextAsync(body, TestContext.Current.CancellationToken);
        var store = new DependencyGraphDocumentStore(new FileWriter());

        // Act.
        var entry = await store.GetOrLoadAsync(body, TestContext.Current.CancellationToken);
        var saved = await store.SaveAsync(body, entry.Document, TestContext.Current.CancellationToken);

        // Assert: what the file holds is unknown, so the empty model is not written over it.
        Assert.False(entry.IsUsable);
        Assert.Empty(entry.Model.Diagram.Elements);
        Assert.True(saved.Failed);
        Assert.Equal(before, await File.ReadAllTextAsync(body, TestContext.Current.CancellationToken));
    }
}
