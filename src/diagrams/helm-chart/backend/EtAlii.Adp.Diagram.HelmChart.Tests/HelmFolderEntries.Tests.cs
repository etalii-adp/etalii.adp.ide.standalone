using Xunit;

namespace EtAlii.Adp.Diagram.HelmChart.Tests;

/// <summary>
/// The stand-in for gap 3 (helm-chart-disl-fbl, Requirement 3.5, findings B4 and B5): the directories no file rule
/// can select, listed so that today's answers for them hold when the folder is read through the binding.
/// </summary>
public sealed class HelmFolderEntriesTests : IDisposable
{
    private readonly TestFolder _folder = TestFolder.Create("EtAlii.Adp.HelmFolderEntries");

    public void Dispose() => _folder.Dispose();

    [Fact]
    public void ASubchartsDeeperDirectory_IsListed_SoItsDeeperCountStaysOne()
    {
        // Arrange: unconventional/charts/deep/charts/deeper holds only marker.txt, which no file rule selects, so
        // without the entry the deeper count of deep would read 0 (finding B4).
        var folder = HelmFolder.Fixture("unconventional");

        // Act.
        var entries = HelmFolderEntries.Of(folder);
        var deep = Assert.Single(HelmFolder.Chart(folder).Vendored, entry => !entry.Sealed);

        // Assert.
        Assert.Equal(["charts/deep/", "charts/deep/charts/deeper/"], entries);
        Assert.Equal(1, deep.DeeperCount);
    }

    [Fact]
    public void EmptyDirectories_AreListed_AndKeepTodaysNodes()
    {
        // Arrange: the three directories git cannot store, as the empty-directories fixture's readme lists them.
        var root = _folder.Combine("chart");
        TestFolder.CopyTree(HelmFolder.Fixture("empty-directories"), root);
        foreach (var directory in new[] { "crds", "charts/hollow", "charts/holder/charts/empty" })
        {
            Directory.CreateDirectory(_folder.Combine("chart", directory));
        }

        // Act.
        var entries = HelmFolderEntries.Of(root);
        var chart = HelmFolder.Chart(root);

        // Assert: an empty crds/ is a CRDs node of no files, a directory under charts/ with no Chart.yaml is a
        // subchart, and the directories among a subchart's charts/ entries are counted beside its file (notes.txt,
        // one/ and empty/: three, as the reader counted entries on disk).
        Assert.Contains("crds/", entries);
        Assert.Contains("charts/hollow/", entries);
        Assert.Contains("charts/holder/charts/empty/", entries);
        Assert.Equal(0, chart.Crds?.FileCount);
        Assert.Contains(chart.Vendored, entry => entry.RelativePath == "charts/hollow");
        Assert.Contains("charts/holder/charts/one/", entries);
        Assert.Equal(3, Assert.Single(chart.Vendored, entry => entry.RelativePath == "charts/holder").DeeperCount);
    }

    [Fact]
    public void AFolderWithNoneOfThem_ListsNothing()
    {
        // Act and assert.
        Assert.Empty(HelmFolderEntries.Of(HelmFolder.Fixture("broken")));
    }

    [Fact]
    public void AFolderThatIsGone_ListsNothing()
    {
        // Act and assert.
        Assert.Empty(HelmFolderEntries.Of(_folder.Combine("gone")));
    }
}
