using Xunit;

namespace EtAlii.Adp.Context.Tests;

/// <summary>
/// A file dialog's tree: the accepted files of each folder by name, or by the rank its asker gives them and then by name
/// (etalii.adp spec 092, FR-025, T035: a knowledge relation offers the tables first and the diagrams after them).
/// </summary>
public sealed class ContextFileTreeTests : IDisposable
{
    private readonly TestFolder _folder = TestFolder.Create("adp-context-file-tree");

    private string Root => _folder.Path;

    public void Dispose() => _folder.Dispose();

    [Fact]
    public async Task TheFilesOfAFolder_AreInTheOrderOfTheirRank_ThenOfTheirName()
    {
        // Arrange: two tables and two diagrams whose names interleave.
        foreach (var name in new[] { "b.tml", "c.yaml", "a.yaml", "d.mm" })
        {
            await File.WriteAllTextAsync(Path.Combine(Root, name), "", TestContext.Current.CancellationToken);
        }

        // Act.
        var byName = ContextFileTree.Build(Root, _ => true);
        var ranked = ContextFileTree.Build(Root, _ => true, path => path.EndsWith(".yaml", StringComparison.Ordinal) ? 0 : 1);

        // Assert.
        Assert.Equal(["a.yaml", "b.tml", "c.yaml", "d.mm"], byName.Select(node => node.Id));
        Assert.Equal(["a.yaml", "c.yaml", "b.tml", "d.mm"], ranked.Select(node => node.Id));
    }
}
