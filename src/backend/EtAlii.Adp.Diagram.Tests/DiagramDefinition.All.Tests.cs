using Xunit;

namespace EtAlii.Adp.Diagram.Tests;

/// <summary>
/// These exercise process-wide static state, so they live in one class (xunit runs the tests
/// of a class one at a time) and each one starts from a reset. No other test class touches
/// <see cref="DiagramDefinition.All"/>.
/// </summary>
public class DiagramDefinitionAllTests : IDisposable
{
    private static readonly DiagramDefinition Sample = new(new DiagramOrigin("fixture", "sample"), "Sample");

    public DiagramDefinitionAllTests()
    {
        DiagramDefinition.ResetForTests();
    }

    public void Dispose()
    {
        DiagramDefinition.ResetForTests();
    }

    [Fact]
    public void All_BeforeInitialize_IsEmptyNotNull()
    {
        Assert.NotNull(DiagramDefinition.All);
        Assert.Empty(DiagramDefinition.All);
        Assert.False(DiagramDefinition.IsInitialized);
    }

    [Fact]
    public void Initialize_RunsTheScanAndFillsAll()
    {
        var filledNow = DiagramDefinition.Initialize(() => [Sample]);

        Assert.True(filledNow);
        Assert.Same(Sample, Assert.Single(DiagramDefinition.All));
        Assert.True(DiagramDefinition.IsInitialized);
    }

    [Fact]
    public void All_ReturnsTheSameCollectionOnEveryRead()
    {
        // Requirement 1.3: a second read must not re-run anything - it is the cached list.
        DiagramDefinition.Initialize(() => [Sample]);

        Assert.Same(DiagramDefinition.All, DiagramDefinition.All);
    }

    [Fact]
    public void Initialize_CalledTwice_DoesNotRunTheSecondScanAndKeepsTheFirstList()
    {
        // A second host in the same process (WebApplicationFactory per test) runs the same
        // startup; the cache is per process, so the first fill stands and the second scan
        // never even runs.
        DiagramDefinition.Initialize(() => [Sample]);
        var secondScanRan = false;

        var filledNow = DiagramDefinition.Initialize(() =>
        {
            secondScanRan = true;
            return [new DiagramDefinition(new DiagramOrigin("fixture", "other"), "Other")];
        });

        Assert.False(filledNow);
        Assert.False(secondScanRan);
        Assert.Same(Sample, Assert.Single(DiagramDefinition.All));
    }

    [Fact]
    public async Task Initialize_UnderConcurrentCallers_RunsTheScanExactlyOnce()
    {
        // Test classes build hosts in parallel, so several can reach Initialize at once.
        var scans = 0;
        var gate = new ManualResetEventSlim();

        var callers = Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
        {
            gate.Wait();
            return DiagramDefinition.Initialize(() =>
            {
                Interlocked.Increment(ref scans);
                return [Sample];
            });
        })).ToArray();
        gate.Set();
        var results = await Task.WhenAll(callers);

        Assert.Equal(1, scans);
        Assert.Equal(1, results.Count(filledNow => filledNow));
        Assert.Single(DiagramDefinition.All);
    }

    [Fact]
    public void IsInitialized_TracksWhetherTheScanRan_NotWhetherAnythingWasFound()
    {
        DiagramDefinition.Initialize(() => []);

        Assert.True(DiagramDefinition.IsInitialized);
        Assert.Empty(DiagramDefinition.All);
        // ...and an empty first result is still the result: no later scan replaces it.
        Assert.False(DiagramDefinition.Initialize(() => [Sample]));
        Assert.Empty(DiagramDefinition.All);
    }

    [Fact]
    public void Initialize_WithNull_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => DiagramDefinition.Initialize(null!));
    }

    [Fact]
    public void Initialize_WhenTheScanReturnsNull_ThrowsAndStaysUninitialized()
    {
        Assert.Throws<InvalidOperationException>(() => DiagramDefinition.Initialize(() => null!));

        Assert.False(DiagramDefinition.IsInitialized);
        Assert.Empty(DiagramDefinition.All);
    }
}
