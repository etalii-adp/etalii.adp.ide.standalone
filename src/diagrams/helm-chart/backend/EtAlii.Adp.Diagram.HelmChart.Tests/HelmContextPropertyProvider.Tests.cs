using EtAlii.Adp.Context;
using Xunit;
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Diagram.HelmChart.Tests;

/// <summary>
/// The read-only grids (Requirement 9.3/9.4): every row read-only with a true reason naming
/// its defining file, and the write path refusing unconditionally.
/// </summary>
/// <remarks>
/// What each row says is the definition's, and is held by the parity transcript, which records every
/// row of every element of the corpus, and by <c>Parity/HelmDerivedRowsTests</c>, which compares each
/// with the hand-written oracle (helm-chart-disl-fbl, task 5). The cases that asserted the chart's
/// summary, a dependency's wiring, an unvendored dependency and a partial's defines are the rows of
/// the <c>well-formed</c> fixture there.
/// </remarks>
public class HelmContextPropertyProviderTests : IDisposable
{
    private readonly string _root;
    private readonly HelmChartStore _store = new(new HelmChartReader(), TimeSpan.FromMilliseconds(150));
    private readonly HelmContextPropertyProvider _provider;

    public HelmContextPropertyProviderTests()
    {
        _root = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.Tests", Guid.NewGuid().ToString("N"));
        TestFolder.CopyTree(IoPath.Combine(AppContext.BaseDirectory, "Fixtures", "well-formed"), _root);
        _provider = new HelmContextPropertyProvider(_store);
    }

    public void Dispose()
    {
        _store.Dispose();
        TestFolder.TryDelete(_root);
    }

    [Fact]
    public async Task EveryRowOfEveryElement_IsReadOnlyWithATrueReason()
    {
        // Arrange.
        var chart = _store.GetOrLoad(_root);
        var graph = HelmGraph.Derive(chart);
        var everything = graph.Nodes.Select(node => node.Id).Concat(HelmElementMapper.Relations(chart).Select(relation => relation.Id)).ToArray();

        // Assert, first, that the graph was derived at all. Without this the foreach below
        // skips silently on an empty graph and the test passes loudest exactly when it has
        // stopped looking at anything.
        Assert.True(
            everything.Length >= 6,
            $"The derived graph produced {everything.Length} elements; this test cannot check rows for elements that do not exist.");

        foreach (var elementId in everything)
        {
            // Act.
            var rows = await _provider.DescribeAsync(Target(elementId), TestContext.Current.CancellationToken);

            // Assert. The floor on rows is the second one this test needs, and it guards a
            // different failure from the one above: a provider that returned no rows for
            // every element would run the whole loop and pass, because Assert.All is
            // vacuous on an empty collection. The graph floor cannot catch that.
            Assert.NotEmpty(rows);
            Assert.All(rows, row =>
            {
                if (row == null!)
                {
                    throw new ArgumentNullException(nameof(row));
                }

                Assert.False(string.IsNullOrEmpty(row.ReadOnlyReason), $"{elementId}/{row.Id} has no reason");
                Assert.Contains("Defined in ", row.ReadOnlyReason, StringComparison.Ordinal);
                Assert.Contains("edit it in a text editor", row.ReadOnlyReason, StringComparison.Ordinal);
            });
        }
    }

    [Fact]
    public async Task SetAsync_RefusesUnconditionally()
    {
        // Act.
        var result = await _provider.SetAsync(Target("chart"), "helm.name", "renamed", TestContext.Current.CancellationToken);

        // Assert.
        Assert.False(result.IsSuccess);
        Assert.Contains("changes nothing", result.Error, StringComparison.Ordinal);
    }

    /// <summary>A selected element's target: the registration it was selected in, beside the chart.</summary>
    private ContextTarget Target(string elementId) => ContextTarget.ForElement(IoPath.Combine(_root, "helm-chart.adp"), elementId, _root);
}
