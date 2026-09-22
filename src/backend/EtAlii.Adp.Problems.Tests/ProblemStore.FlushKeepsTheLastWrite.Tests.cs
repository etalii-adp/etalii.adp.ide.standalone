using EtAlii.Adp.Common;
using EtAlii.Adp.Hierarchy;
using EtAlii.Adp.TestSupport;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Problems.Tests;

/// <summary>
/// The last change to a project's problems survives the store being disposed, whichever of the
/// flush and the debounce callback gets there first.
/// </summary>
/// <remarks>
/// <para>
/// <b>The defect, found from a gate red.</b> <c>Dispose</c> sets <c>_disposed</c> BEFORE its flush
/// loop reaches an entry. A debounce callback waking in that gap took the entry's gate, cleared
/// <c>WriteTimer</c>, read <c>_disposed</c> as "the flush has already written it" and returned - and
/// the flush then read the cleared timer as "nothing owed" and skipped the entry. <b>Neither wrote,
/// and the verdict was lost</b>, which is what
/// <c>ProblemStoreRegistryStalenessTests.AnUnknownTypeVerdict_StaysFresh_WhileTheTypeIsStillUnknown</c>
/// saw as "the collection was empty" in a 5,098-test gate run. The entry now records the DEBT, which
/// neither party can clear without writing.
/// </para>
/// <para>
/// <b>Why a seam and not a race.</b> The gap is a few instructions wide: the same project passed
/// five runs out of five alone, and the full suite hit it once. The guard drives the exact
/// interleaving through the same code the timer calls, so it fails for the reason it names rather
/// than when the machine is loaded enough.
/// </para>
/// </remarks>
public class ProblemStoreFlushKeepsTheLastWriteTests : IDisposable
{
    private static readonly DiagramOrigin Mindmap = new("freeplane", "mindmap");
    private static readonly DiagramDefinition MindmapDefinition = new(Mindmap, "Mind map", Extension: ".mm");

    private readonly string _scratch = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.Tests", Guid.NewGuid().ToString("N"));
    private readonly string _appData;
    private readonly string _root;

    public ProblemStoreFlushKeepsTheLastWriteTests()
    {
        _appData = IoPath.Combine(_scratch, "appdata");
        _root = IoPath.Combine(_scratch, "project");
        Directory.CreateDirectory(_appData);
        Directory.CreateDirectory(_root);
        File.WriteAllText(IoPath.Combine(_root, "services.adp"), "dotnet/dependency-graph\n");
        File.WriteAllText(IoPath.Combine(_root, "services.dgr"), "the document");
    }

    public void Dispose()
    {
        TestFolder.TryDelete(_scratch);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void ADebounceCallbackWakingInsideDispose_DoesNotCostTheLastWrite()
    {
        // Arrange: a change is owed, and the callback is driven in the one gap that loses it -
        // after Dispose closes the store to new schedules, before its flush reaches this entry.
        using (var store = Store())
        {
            store.ReplaceFor(_root, ["services.adp"], [Verdict()]);
            var entry = store.EntryFor(_root);
            store.BetweenClosingAndFlushing = () => store.OnDebounceElapsed(entry);
        }

        // Assert: a restart reads the verdict back. Through a second store rather than off disk,
        // because what the panel shows is the question.
        using var afterTheRestart = Store();
        var set = afterTheRestart.Get(_root);
        Assert.Single(set.Problems);
    }

    [Fact]
    public void AnOrdinaryDispose_StillKeepsThePendingWrite()
    {
        // The floor: the guard above must not pass merely because something writes on the way out.
        using (var store = Store())
        {
            store.ReplaceFor(_root, ["services.adp"], [Verdict()]);
        }

        using var afterTheRestart = Store();
        Assert.Single(afterTheRestart.Get(_root).Problems);
    }

    [Fact]
    public void ACallbackWakingAfterTheFlushHasWritten_WritesNothingMore()
    {
        // The must-not-catch half, and the behaviour the disposed check was added for: once the
        // debt is settled, a late callback has nothing to do and must not write into a root whose
        // owner may already have taken it away.
        var store = Store();
        store.ReplaceFor(_root, ["services.adp"], [Verdict()]);
        var entry = store.EntryFor(_root);
        store.Dispose();

        var cachePath = Directory.EnumerateFiles(_appData, "*.json", SearchOption.AllDirectories).Single();
        var written = File.GetLastWriteTimeUtc(cachePath);
        var content = File.ReadAllText(cachePath);

        store.OnDebounceElapsed(entry);

        Assert.Equal(written, File.GetLastWriteTimeUtc(cachePath));
        Assert.Equal(content, File.ReadAllText(cachePath));
    }

    private ProblemStore Store() =>
        new(_appData,
            new DiagramFileRouter(new TestDiagramDefinitionCatalog([MindmapDefinition])),
            new DiagramValidators([]),
            // Long enough that the real timer never fires during the test: the interleaving is
            // driven through the seam, so nothing here depends on how fast the machine is.
            writeDelay: TimeSpan.FromMinutes(5));

    private static StoredProblem Verdict() =>
        new(
            new DiagramProblem(DiagramProblemSeverity.Error, "not a known diagram type", null),
            RelativePath: "services.adp",
            LastWriteTimeUtc: DateTime.UtcNow,
            Length: 42,
            RulesVersion: "",
            Stale: false);
}
