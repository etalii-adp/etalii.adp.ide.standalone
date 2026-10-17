using EtAlii.Adp.Documents;
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

    protected override IDependencyGraphDocumentStore CreateStore() => new DependencyGraphDocumentStore();

    protected override void Edit(DependencyGraphBody document) =>
        document.Change(new ModelChange.Set(document.Model.Elements.First(element => element.Type == "Node").Id, new Dictionary<string, object?> { ["label"] = EditMarker }));

    protected override string TextOf(DependencyGraphBody document) => document.Text;

    protected override bool IsEmpty(DislModel model) => model.Diagram.Elements.Count == 0;

    [Fact]
    public void AGraphThatIsNotYaml_IsHeldUnreadable_AndASaveIsRefusedUnwritten()
    {
        // Arrange.
        using var folder = TestFolder.Create("dependency-graph-store");
        var body = folder.Combine("broken.dgr");
        File.WriteAllText(body, "elements: [\n  - id: a\n");
        var before = File.ReadAllText(body);
        var store = new DependencyGraphDocumentStore();

        // Act.
        var entry = store.GetOrLoad(body);
        var saved = store.Save(body, entry.Document);

        // Assert: what the file holds is unknown, so the empty model is not written over it.
        Assert.False(entry.IsUsable);
        Assert.Empty(entry.Model.Diagram.Elements);
        Assert.True(saved.Failed);
        Assert.Equal(before, File.ReadAllText(body));
    }
}
