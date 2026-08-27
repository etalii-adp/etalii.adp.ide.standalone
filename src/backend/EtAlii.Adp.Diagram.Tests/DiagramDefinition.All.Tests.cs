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
        // Arrange, act and assert.
        Assert.NotNull(DiagramDefinition.All);
        Assert.Empty(DiagramDefinition.All);
        Assert.False(DiagramDefinition.IsInitialized);
    }

    [Fact]
    public void Initialize_RunsTheScanAndFillsAll()
    {
        // Act.
        var filledNow = DiagramDefinition.Initialize(() => [Sample]);

        // Assert.
        Assert.True(filledNow);
        Assert.Same(Sample, Assert.Single(DiagramDefinition.All));
        Assert.True(DiagramDefinition.IsInitialized);
    }

    [Fact]
    public void All_ReturnsTheSameCollectionOnEveryRead()
    {
        // Act.
        // Requirement 1.3: a second read must not re-run anything - it is the cached list.
        DiagramDefinition.Initialize(() => [Sample]);

        // Assert.
        Assert.Same(DiagramDefinition.All, DiagramDefinition.All);
    }

    [Fact]
    public void Initialize_CalledTwice_DoesNotRunTheSecondScanAndKeepsTheFirstList()
    {
        // Arrange.
        // A second host in the same process (WebApplicationFactory per test) runs the same
        // startup; the cache is per process, so the first fill stands and the second scan
        // never even runs.
        DiagramDefinition.Initialize(() => [Sample]);
        var secondScanRan = false;

        // Act.
        var filledNow = DiagramDefinition.Initialize(() =>
        {
            secondScanRan = true;
            return [new DiagramDefinition(new DiagramOrigin("fixture", "other"), "Other")];
        });

        // Assert.
        Assert.False(filledNow);
        Assert.False(secondScanRan);
        Assert.Same(Sample, Assert.Single(DiagramDefinition.All));
    }

    [Fact]
    public async Task Initialize_UnderConcurrentCallers_RunsTheScanExactlyOnce()
    {
        // Arrange.
        // Test classes build hosts in parallel, so several can reach Initialize at once.
        var scans = 0;
        var gate = new ManualResetEventSlim();
        // Captured once rather than read inside each task: the same token either way, but it
        // reads as one decision and keeps the gate's wait and the tasks on the same footing.
        var cancellationToken = TestContext.Current.CancellationToken;

        // Act.
        var callers = Enumerable.Range(0, 8).Select(_ => Task.Run(
            () =>
            {
                gate.Wait(cancellationToken);
                return DiagramDefinition.Initialize(() =>
                {
                    Interlocked.Increment(ref scans);
                    return [Sample];
                });
            },
            cancellationToken)).ToArray();
        gate.Set();
        var results = await Task.WhenAll(callers);

        // Assert.
        Assert.Equal(1, scans);
        Assert.Equal(1, results.Count(filledNow => filledNow));
        Assert.Single(DiagramDefinition.All);
    }

    [Fact]
    public void IsInitialized_TracksWhetherTheScanRan_NotWhetherAnythingWasFound()
    {
        // Act.
        DiagramDefinition.Initialize(() => []);

        // Assert.
        Assert.True(DiagramDefinition.IsInitialized);
        Assert.Empty(DiagramDefinition.All);
        // ...and an empty first result is still the result: no later scan replaces it.
        Assert.False(DiagramDefinition.Initialize(() => [Sample]));
        Assert.Empty(DiagramDefinition.All);
    }

    [Fact]
    public void Initialize_WithNull_Throws()
    {
        // Arrange, act and assert.
        Assert.Throws<ArgumentNullException>(() => DiagramDefinition.Initialize());
    }

    [Fact]
    public void Initialize_WhenTheScanReturnsNull_ThrowsAndStaysUninitialized()
    {
        // Act.
        Assert.Throws<InvalidOperationException>(() => DiagramDefinition.Initialize(() => null!));

        // Assert.
        Assert.False(DiagramDefinition.IsInitialized);
        Assert.Empty(DiagramDefinition.All);
    }
}
