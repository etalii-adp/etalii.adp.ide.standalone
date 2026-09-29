using EtAlii.Adp.Context;
using EtAlii.Adp.Documents.Wire;
using Xunit;
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Diagram.HelmChart.Tests;

/// <summary>
/// The read-only grids (Requirement 9.3/9.4): every row read-only with a true reason naming
/// its defining file, and the write path refusing unconditionally.
/// </summary>
public class HelmContextPropertyProviderTests : IDisposable
{
    private readonly string _root;
    private readonly HelmChartStore _store = new(new HelmChartReader(), TimeSpan.FromMilliseconds(150));
    private readonly HelmContextPropertyProvider _provider;

    public HelmContextPropertyProviderTests()
    {
        _root = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.Tests", Guid.NewGuid().ToString("N"));
        CopyTree(IoPath.Combine(AppContext.BaseDirectory, "Fixtures", "well-formed"), _root);
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
        var everything = graph.Nodes.Select(node => node.Id).Concat(graph.Edges.Select(edge => edge.Id)).ToArray();

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
    public async Task TheChartNode_CarriesTheSummary()
    {
        // Act.
        var rows = await _provider.DescribeAsync(Target("chart"), TestContext.Current.CancellationToken);

        // Assert.
        Assert.Equal("shop", Assert.Single(rows, row => row.Id == "helm.name").Value);
        Assert.Equal("1.4.0", Assert.Single(rows, row => row.Id == "helm.version").Value);
        // The five files under templates/: deployment, service, _helpers, NOTES, tests/.
        Assert.Equal("5", Assert.Single(rows, row => row.Id == "helm.templates").Value);
        Assert.Equal("2", Assert.Single(rows, row => row.Id == "helm.dependencies").Value);
        Assert.Equal("2", Assert.Single(rows, row => row.Id == "helm.values-layers").Value);
    }

    [Fact]
    public async Task ADependency_ShowsItsWiring()
    {
        // Act.
        var rows = await _provider.DescribeAsync(Target("dep:cache"), TestContext.Current.CancellationToken);

        // Assert.
        Assert.Equal("redis", Assert.Single(rows, row => row.Id == "helm.name").Value);
        Assert.Equal("cache", Assert.Single(rows, row => row.Id == "helm.alias").Value);
        var condition = Assert.Single(rows, row => row.Id == "helm.condition");
        Assert.Contains("cache.enabled", condition.Value, StringComparison.Ordinal);
        Assert.Contains("currently on", condition.Value, StringComparison.Ordinal);
        Assert.Equal("17.3.2", Assert.Single(rows, row => row.Id == "helm.pinned").Value);
        Assert.Contains("resolved", Assert.Single(rows, row => row.Id == "helm.resolution").Value, StringComparison.Ordinal);
        // The declaring file is the reason's named source: a dependency has no file of its own.
        Assert.Contains("Chart.yaml", condition.ReadOnlyReason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnUnvendoredDependency_SaysWhereTheAnswerWouldComeFrom()
    {
        // Act.
        var rows = await _provider.DescribeAsync(Target("dep:postgres"), TestContext.Current.CancellationToken);

        // Assert.
        var resolution = Assert.Single(rows, row => row.Id == "helm.resolution");
        Assert.Contains("unvendored", resolution.Value, StringComparison.Ordinal);
        Assert.Contains("helm dependency build", resolution.Value, StringComparison.Ordinal);
    }

    [Fact]
    public async Task APartial_ListsWhatItDefines()
    {
        // Act.
        var rows = await _provider.DescribeAsync(Target("tpl:templates/_helpers.tpl"), TestContext.Current.CancellationToken);

        // Assert.
        Assert.Contains("partial", Assert.Single(rows, row => row.Id == "helm.role").Value, StringComparison.Ordinal);
        Assert.Equal("shop.fullname, shop.labels", Assert.Single(rows, row => row.Id == "helm.defines").Value);
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

    private ContextTarget Target(string elementId) => new(
        ContextScope.DiagramElement,
        _root,
        IsContainer: false,
        SourceId: default,
        _root,
        default,
        elementId);

    private static void CopyTree(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(IoPath.Combine(destination, IoPath.GetRelativePath(source, directory)));
        }

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            File.Copy(file, IoPath.Combine(destination, IoPath.GetRelativePath(source, file)));
        }
    }
}
