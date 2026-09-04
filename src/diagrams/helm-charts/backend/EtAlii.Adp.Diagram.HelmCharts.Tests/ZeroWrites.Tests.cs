using EtAlii.Adp.Backend;
using EtAlii.Adp.Backend.Context;
using EtAlii.Adp.Backend.Diagrams;

using Microsoft.Extensions.DependencyInjection;

using Xunit;

using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Diagram.HelmCharts.Tests;

/// <summary>
/// The claim this diagram type turns on: it reads a chart and changes <b>nothing</b> in it
/// (Requirement 7.1) - trivially, because the module has no writer for chart content at all.
/// The one write the type has, a reposition, goes through CORE's layout command into the
/// <c>.adp</c>, and the session tests prove the chart's own files survive that byte-for-byte.
/// </summary>
/// <remarks>
/// The design says the requirement is satisfied by construction. This test exists anyway: a
/// claim this central deserves a guard rather than an argument, and the guard's real job is
/// the future - it fails the day somebody adds a write path, which is exactly when nobody is
/// thinking about this requirement. It exercises the real components, refused writes included:
/// a refusal that still touched the disk is the bug actually worth guarding against.
/// </remarks>
public class ZeroWritesTests : IDisposable
{
    private static readonly TimeSpan SettleDelay = TimeSpan.FromMilliseconds(150);

    private readonly string _root;
    private readonly HelmChartStore _store = new(new HelmChartReader(), SettleDelay);
    private readonly ServiceProvider _provider = new ServiceCollection().AddCommands().AddHelmCharts().BuildServiceProvider();

    public ZeroWritesTests()
    {
        _root = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.Tests", Guid.NewGuid().ToString("N"));
        CopyTree(IoPath.Combine(AppContext.BaseDirectory, "Fixtures", "well-formed"), _root);
        File.WriteAllText(IoPath.Combine(_root, "helm-chart.adp"), "helm/chart\r\n");
    }

    public void Dispose()
    {
        _provider.Dispose();
        _store.Dispose();
        TestFolder.TryDelete(_root);
    }

    [Fact]
    public async Task EveryReadPath_LeavesTheChartByteForByteUnchanged()
    {
        // Arrange.
        // Contents AND timestamps: a file rewritten with identical bytes is still a file this
        // module wrote, and would show up in someone's git status as a touched mtime. The
        // .adp is included - browsing must not write layout, only a deliberate drag may.
        var before = Snapshot();

        var registration = IoPath.Combine(_root, "helm-chart.adp");
        var factory = new HelmSessionFactory(
            _store, new HelmElementMapper(), _provider.GetRequiredService<IHistoryStackStore>());

        // Act.
        // Everything this module can be asked to do short of a drag, in a user's order.
        await using (var session = factory.Open(ShortGuid.NewShortGuid(), _root, registration, registration))
        {
            session.Baseline();
            session.UpdateView(new DiagramViewport(-50, -50, 200, 200));

            var chart = _store.GetOrLoad(_root);
            var graph = HelmGraph.Derive(chart);

            // Select and describe every node and every edge there is.
            var properties = new HelmContextPropertyProvider(_store);
            var everything = graph.Nodes.Select(node => node.Id).Concat(graph.Edges.Select(edge => edge.Id)).ToArray();

            // The walk has to find the graph, or the read paths below are never exercised
            // and the chart is trivially unchanged - this test would report zero writes
            // loudest exactly when it had stopped reading anything.
            Assert.True(
                everything.Length >= 6,
                $"The derived graph produced {everything.Length} elements; this test cannot exercise read paths for elements that do not exist.");

            foreach (var id in everything)
            {
                var target = new ContextTarget(ContextScope.DiagramElement, _root, false, default, _root, default, id);
                await properties.DescribeAsync(target, TestContext.Current.CancellationToken);

                // The refused write. Included on purpose: this is the path most likely to
                // grow a file operation by accident one day.
                await properties.SetAsync(target, "helm.name", "changed", TestContext.Current.CancellationToken);
            }

            // The other refusals: a reparent, and a move of something that has no position.
            await session.MoveElementAsync("chart", "values:values.yaml", 0, TestContext.Current.CancellationToken);
            await session.MoveElementToAsync("edge:a|Declares|b|x", 1, 2, TestContext.Current.CancellationToken);

            // And the rules, which read the whole folder again through their own reader.
            await new HelmValidator().ValidateAsync(
                new DiagramValidationRequest("helm/chart\r\n", "helm-chart", _root, registration, registration)
                {
                    SubjectFolder = _root,
                },
                TestContext.Current.CancellationToken);
        }

        // Assert.
        AssertUnchanged(before, Snapshot());
    }

    [Fact]
    public void ReadingAFolder_CreatesNoFileOfItsOwn()
    {
        // Arrange.
        var before = Directory.GetFileSystemEntries(_root, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal).ToArray();

        // Act.
        // No cache file, no index, no sidecar - the .adp is the only file ADP contributes to
        // the folder, and it was already there.
        _ = HelmRuleSet.Judge(new HelmChartReader().Read(_root));

        // Assert.
        Assert.Equal(before, Directory.GetFileSystemEntries(_root, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task ReadingDoesNotLockTheChart_SoAnEditorCanStillSaveOverIt()
    {
        // Arrange.
        // The only editor this diagram type ever expects a user to have open is a text editor
        // on these very files. Holding an exclusive handle would fight it.
        var registration = IoPath.Combine(_root, "helm-chart.adp");
        var factory = new HelmSessionFactory(
            _store, new HelmElementMapper(), _provider.GetRequiredService<IHistoryStackStore>());
        await using var session = factory.Open(ShortGuid.NewShortGuid(), _root, registration, registration);
        session.Baseline();

        // Act and assert.
        var values = IoPath.Combine(_root, "values.yaml");
        var exception = Record.Exception(() => File.AppendAllText(values, "\n# edited while open\n"));
        Assert.Null(exception);
    }

    [Fact]
    public void TheModulesPublicSurface_NamesNoVerbThatWrites()
    {
        // Arrange.
        // Aimed at the future: this module's public surface should never grow a verb that
        // writes chart content. If one appears, this names it rather than leaving a reviewer
        // to notice. Crude, and the right kind of crude for a claim that has to survive people.
        var writeVerbs = new[] { "Save", "Write", "Delete", "Create", "Rename", "Update" };

        // Act.
        var named = typeof(HelmChartStore).Assembly.GetExportedTypes()
            // The generated wire types are excluded: protobuf gives every message a WriteTo,
            // which serializes to a stream and has nothing to do with a file.
            .Where(type => !(type.Namespace ?? "").EndsWith(".Wire", StringComparison.Ordinal))
            .SelectMany(type => type
                .GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly)
                .Select(method => $"{type.Name}.{method.Name}"))
            .Where(name => writeVerbs.Any(verb => name.Contains(verb, StringComparison.Ordinal)))
            .Order(StringComparer.Ordinal)
            .ToArray();

        // Assert.
        // MoveElementToAsync is deliberately not caught by the list: it is core's seam, its
        // write goes to the .adp through the core command, and the session tests prove the
        // chart's own files survive it byte-for-byte.
        Assert.Empty(named);
    }

    /// <summary>
    /// Compares two snapshots and says exactly which file broke the promise - never tuple or
    /// record equality over byte arrays, which compares references and reports nothing usable.
    /// </summary>
    private static void AssertUnchanged(
        Dictionary<string, (byte[] Bytes, DateTime Written)> before,
        Dictionary<string, (byte[] Bytes, DateTime Written)> after)
    {
        var appeared = after.Keys.Except(before.Keys, StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal).ToArray();
        var vanished = before.Keys.Except(after.Keys, StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal).ToArray();
        Assert.True(appeared.Length == 0, $"The module created: {string.Join(", ", appeared)}");
        Assert.True(vanished.Length == 0, $"The module removed: {string.Join(", ", vanished)}");

        foreach (var (path, expected) in before.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            var (actualBytes, actualWritten) = after[path];
            Assert.True(expected.Bytes.SequenceEqual(actualBytes), $"The module rewrote the contents of {path}.");
            Assert.True(
                expected.Written == actualWritten,
                $"The module touched {path}: written {expected.Written:O} before, {actualWritten:O} after.");
        }
    }

    private Dictionary<string, (byte[] Bytes, DateTime Written)> Snapshot() =>
        Directory.GetFiles(_root, "*", SearchOption.AllDirectories)
            .ToDictionary(path => path, path => (File.ReadAllBytes(path), File.GetLastWriteTimeUtc(path)));

    private static void CopyTree(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var folder in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(IoPath.Combine(destination, IoPath.GetRelativePath(source, folder)));
        }
        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            File.Copy(file, IoPath.Combine(destination, IoPath.GetRelativePath(source, file)));
        }
    }
}
