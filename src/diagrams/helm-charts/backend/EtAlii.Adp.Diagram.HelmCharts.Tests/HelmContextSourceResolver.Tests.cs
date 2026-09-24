using EtAlii.Adp.Documents;
using EtAlii.Adp.Documents.Wire;
using EtAlii.Adp.Context;
using EtAlii.Adp.Hierarchy;
using Xunit;
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Diagram.HelmCharts.Tests;

/// <summary>
/// Making a chart node selectable - and refusing to record a selection that cannot be
/// verified (Requirements 8, 9.3).
/// </summary>
public class HelmContextSourceResolverTests : IDisposable
{
    private static readonly TimeSpan SettleDelay = TimeSpan.FromMilliseconds(150);
    private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(15);

    private readonly string _root;
    private readonly string _registration;
    private readonly HelmChartStore _store = new(new HelmChartReader(), SettleDelay);
    private readonly HelmContextSourceResolver _resolver;

    public HelmContextSourceResolverTests()
    {
        _root = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(IoPath.Combine(_root, "templates"));
        File.WriteAllText(IoPath.Combine(_root, "Chart.yaml"),
            "apiVersion: v2\nname: resolved\nversion: 1.0.0\ndependencies:\n  - name: redis\n    version: 1.0.0\n");
        File.WriteAllText(IoPath.Combine(_root, "values.yaml"), "replicaCount: 1\n");
        File.WriteAllText(IoPath.Combine(_root, "templates", "deployment.yaml"), "kind: Deployment\napiVersion: apps/v1\n");
        _registration = IoPath.Combine(_root, "helm-chart.adp");
        File.WriteAllText(_registration, "helm/chart\n");

        // A mindmap definition beside this module's own, so the "not ours" path has a real
        // other type to be rejected in favour of rather than a hypothetical one.
        var catalog = new HelmTestDiagramDefinitionCatalog(
            Diagram.HelmCharts,
            new DiagramDefinition(new DiagramOrigin("freeplane", "mindmap"), "Mind map", Extension: ".mm"));
        _resolver = new HelmContextSourceResolver(new DiagramFileRouter(catalog), _store);
    }

    public void Dispose()
    {
        _store.Dispose();
        TestFolder.TryDelete(_root);
    }

    [Fact]
    public void ItAnswersForElementIds_AndNothingElse()
    {
        // Arrange, act and assert.
        Assert.True(_resolver.CanResolve(new ContextSource { ElementId = new ElementId { Value = "values:values.yaml" } }));
        Assert.False(_resolver.CanResolve(new ContextSource { EntryId = ShortGuid.NewShortGuid() }));
    }

    [Fact]
    public async Task ANode_ResolvesToItsOwnPathAndName()
    {
        // Act.
        var resolution = await Resolve("tpl:templates/deployment.yaml");

        // Assert.
        var level = Assert.IsType<ResolvedContextLevel>(resolution).Level;
        Assert.Equal(ContextScope.DiagramElement, level.Scope);
        Assert.Equal(["templates", "deployment.yaml"], level.RelativePath);
        Assert.Equal("deployment.yaml", level.Detail.Element.Text);
        Assert.Equal("tpl:templates/deployment.yaml", level.Target.ElementId);
        // The target's own path is the chart FOLDER - what everything downstream keys on.
        Assert.Equal(IoPath.GetFullPath(_root), IoPath.GetFullPath(level.Target.ResolvedFullPath));
    }

    [Fact]
    public async Task ADependency_AnswersAsTheFileThatDeclaresIt()
    {
        // Act.
        // A dependency has no file of its own; the declaration is what a reader opens.
        var resolution = await Resolve("dep:redis");

        // Assert.
        var level = Assert.IsType<ResolvedContextLevel>(resolution).Level;
        Assert.Equal(["Chart.yaml"], level.RelativePath);
        Assert.Equal("redis", level.Detail.Element.Text);
    }

    [Fact]
    public async Task AnEdge_ResolvesAsItsDeclaringSide()
    {
        // Arrange.
        var graph = HelmGraph.Derive(_store.GetOrLoad(_root));
        var declares = graph.Edges.Single(edge => edge.Kind == HelmEdgeKind.Declares);

        // Act.
        var resolution = await Resolve(declares.Id);

        // Assert.
        var level = Assert.IsType<ResolvedContextLevel>(resolution).Level;
        Assert.Equal(["Chart.yaml"], level.RelativePath);
        Assert.Contains("declares", level.Detail.Element.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnUnknownElement_IsRejected()
    {
        // Act.
        var resolution = await Resolve("dep:no-such-dependency");

        // Assert.
        Assert.Equal("Unknown element.", Assert.IsType<RejectedContextLevel>(resolution).Reason);
    }

    [Fact]
    public async Task AnElementWithNoDiagramAboveIt_IsRejected()
    {
        // Act.
        // An unverifiable selection is never recorded.
        var resolution = await _resolver.ResolveAsync(
            ShortGuid.NewShortGuid(), _root, ContextSelectionSource.DiagramCanvas,
            new ContextSource { ElementId = new ElementId { Value = "chart" } },
            [], parent: null, TestContext.Current.CancellationToken);

        // Assert.
        Assert.Contains("within its diagram", Assert.IsType<RejectedContextLevel>(resolution).Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AFileOfAnotherType_IsRejectedPlainly()
    {
        // Arrange.
        var other = IoPath.Combine(_root, "map.adp");
        await File.WriteAllTextAsync(other, "freeplane/mindmap\n", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(IoPath.Combine(_root, "map.mm"), "<map><node TEXT=\"a\"/></map>", TestContext.Current.CancellationToken);

        // Act.
        var resolution = await Resolve("chart", registration: other);

        // Assert.
        Assert.Contains("not a Helm chart diagram", Assert.IsType<RejectedContextLevel>(resolution).Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AClientPathThatDisagrees_IsRejected()
    {
        // Act.
        // The client's version of the path is checked, never trusted.
        var resolution = await Resolve("values:values.yaml", clientPath: ["templates", "deployment.yaml"]);

        // Assert.
        Assert.Contains("does not match", Assert.IsType<RejectedContextLevel>(resolution).Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ADeletedValuesFile_ClearsTheSelection()
    {
        // Arrange.
        // The watcher watches the chart root ITSELF - one folder up would hear nothing, the
        // exact bug the sibling module's test caught and this one pins for helm.
        var level = Assert.IsType<ResolvedContextLevel>(await Resolve("values:values.yaml")).Level;
        var cleared = new TaskCompletionSource<bool>();
        using var subscription = _resolver.Track(ShortGuid.NewShortGuid(), _root, level, path =>
        {
            if (path is null)
            {
                cleared.TrySetResult(true);
            }
        });

        // Act.
        File.Delete(IoPath.Combine(_root, "values.yaml"));

        // Assert.
        Assert.True(await cleared.Task.WaitAsync(WaitLimit, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ADisposedSubscription_StopsAnnouncing()
    {
        // Arrange.
        var level = Assert.IsType<ResolvedContextLevel>(await Resolve("values:values.yaml")).Level;
        var announcements = 0;
        var subscription = _resolver.Track(ShortGuid.NewShortGuid(), _root, level, _ => Interlocked.Increment(ref announcements));

        // Act.
        subscription.Dispose();
        File.Delete(IoPath.Combine(_root, "values.yaml"));
        await Task.Delay(SettleDelay + SettleDelay + SettleDelay, TestContext.Current.CancellationToken);

        // Assert.
        Assert.Equal(0, announcements);
    }

    private async Task<ContextLevelResolution> Resolve(
        string elementId, IReadOnlyList<string>? clientPath = null, string? registration = null)
    {
        var parent = new ContextResolvedLevel(
            ContextSelectionSource.Explorer,
            new ContextSource { EntryId = ShortGuid.NewShortGuid() },
            [],
            ContextScope.Hierarchy,
            new ContextTarget(ContextScope.Hierarchy, registration ?? _registration, IsContainer: false, SourceId: default, _root),
            new ContextLevelDetail(),
            null!);

        return await _resolver.ResolveAsync(
            ShortGuid.NewShortGuid(), _root, ContextSelectionSource.DiagramCanvas,
            new ContextSource { ElementId = new ElementId { Value = elementId } },
            clientPath ?? [], parent, TestContext.Current.CancellationToken);
    }
}
