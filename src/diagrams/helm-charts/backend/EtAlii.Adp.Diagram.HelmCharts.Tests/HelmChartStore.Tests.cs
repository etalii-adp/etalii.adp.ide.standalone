using Xunit;

using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Diagram.HelmCharts.Tests;

/// <summary>
/// The watched-folder store (Requirement 1.4): reading, claiming, coalesced re-reads. Burst
/// assertions are ranges, never exact counts - the timing lesson the sibling module recorded.
/// </summary>
public class HelmChartStoreTests : IDisposable
{
    private static readonly TimeSpan SettleDelay = TimeSpan.FromMilliseconds(150);
    private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(10);

    private readonly string _scratch;

    public HelmChartStoreTests()
    {
        _scratch = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_scratch);
        File.WriteAllText(IoPath.Combine(_scratch, "Chart.yaml"), "apiVersion: v2\nname: watched\nversion: 1.0.0\n");
    }

    public void Dispose()
    {
        TestFolder.TryDelete(_scratch);
    }

    [Fact]
    public void GetOrLoad_ReadsOnceAndAnswersFromTheStore()
    {
        // Arrange.
        using var store = new HelmChartStore(new HelmChartReader(), SettleDelay);

        // Act.
        var first = store.GetOrLoad(_scratch);
        var second = store.GetOrLoad(_scratch);

        // Assert.
        Assert.True(first.IsChart);
        Assert.Same(first, second);
    }

    [Fact]
    public void Get_NeverLoads()
    {
        // Arrange.
        using var store = new HelmChartStore(new HelmChartReader(), SettleDelay);

        // Act & Assert.
        Assert.Null(store.Get(_scratch));
    }

    [Fact]
    public void AChangeOnDisk_RaisesOneReRead_WithTheNewChart()
    {
        // Arrange.
        using var store = new HelmChartStore(new HelmChartReader(), SettleDelay);
        var loaded = store.Acquire(_scratch);
        Assert.Empty(loaded.Values);
        using var settled = new ManualResetEventSlim();
        HelmChart? announced = null;
        store.Changed += (_, e) =>
        {
            announced = e.Chart;
            settled.Set();
        };

        // Act.
        File.WriteAllText(IoPath.Combine(_scratch, "values.yaml"), "replicaCount: 1\n");

        // Assert.
        Assert.True(settled.Wait(WaitLimit), "The store never announced the change.");
        Assert.NotNull(announced);
        Assert.Single(announced.Values);
        // The store answers with the new chart from the moment of the event.
        Assert.Same(announced, store.Get(_scratch));
        store.Release(_scratch);
    }

    [Fact]
    public void ABurstOfChanges_CoalescesIntoFewerReReads()
    {
        // Arrange.
        using var store = new HelmChartStore(new HelmChartReader(), SettleDelay);
        store.Acquire(_scratch);
        var changes = 0;
        using var settled = new ManualResetEventSlim();
        store.Changed += (_, _) =>
        {
            Interlocked.Increment(ref changes);
            settled.Set();
        };

        // Act.
        // A helm dependency build rewriting charts/ wholesale is the real-world shape of this.
        const int burst = 12;
        for (var i = 0; i < burst; i++)
        {
            File.WriteAllText(IoPath.Combine(_scratch, $"values-{i}.yaml"), $"layer: {i}\n");
        }

        Assert.True(settled.Wait(WaitLimit), "The store never announced the burst.");
        // Let any trailing timer fire before counting.
        Thread.Sleep(SettleDelay + SettleDelay);

        // Assert.
        // A range, never an exact count: the point is coalescing, not a precise number.
        Assert.InRange(Volatile.Read(ref changes), 1, burst - 1);
        store.Release(_scratch);
    }

    [Fact]
    public void ReleasingTheLastClaim_DropsTheFolder()
    {
        // Arrange.
        using var store = new HelmChartStore(new HelmChartReader(), SettleDelay);
        store.Acquire(_scratch);
        store.Acquire(_scratch);

        // Act & Assert.
        store.Release(_scratch);
        Assert.NotNull(store.Get(_scratch)); // One claim remains.
        store.Release(_scratch);
        Assert.Null(store.Get(_scratch)); // The last one left; the next ask reads afresh.
    }

    [Fact]
    public void ThereIsNoSavePath()
    {
        // Assert.
        // Structural, per Requirement 7.1: the store's public surface has nothing that takes
        // content to write - reflection proves the absence rather than a comment claiming it.
        var writers = typeof(IHelmChartStore).GetMethods()
            .Where(method => method.Name.Contains("Save", StringComparison.OrdinalIgnoreCase)
                             || method.Name.Contains("Write", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        Assert.Empty(writers);
    }
}
