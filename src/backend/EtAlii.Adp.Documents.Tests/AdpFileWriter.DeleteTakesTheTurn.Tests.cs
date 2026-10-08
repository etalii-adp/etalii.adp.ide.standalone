using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Documents.Tests;

/// <summary>
/// A delete of a published path takes the same turn a save of it takes, and says it happened.
/// </summary>
/// <remarks>
/// <para>
/// <b>Measured, and it is a fix rather than an instrument.</b> 400 replaces racing a concurrent
/// delete-and-recreate of the destination failed 305 times: <c>0x800700B7</c> 148,
/// <c>0x80070002</c> 76, <c>0x80070497</c> 75 - the gate flake's own code - <c>0x80070005</c> 5 and
/// <c>0x80070020</c> once. With the delete taking the save's turn, 400 of 400 succeeded and nothing
/// failed at all. The per-destination lock had been serialising writers against writers only, and a
/// deleter was never a writer to it.
/// </para>
/// <para>
/// <b>Why the race itself is not a guard here.</b> A raced delete-against-save test PASSED against
/// a sabotaged Delete that skipped the turn: the deleting thread spends its time inside its own
/// turn-taking save, so the unserialised delete rarely lands in the window. A test that passes
/// against the defect is worse than none, so it was removed rather than kept as reassurance - the
/// race is measured in the numbers above, and the serialisation is pinned deterministically below.
/// </para>
/// <para>
/// The production deleters this covers were calling <c>File.Delete</c> directly:
/// <c>DeleteEntryCommandHandler</c> (a body, its siblings and its registration),
/// <c>C4LayoutSidecar</c>, <c>AddC4ViewCommand</c> and <c>WardleyIdentities</c>.
/// </para>
/// </remarks>
public class AdpFileWriterDeleteTakesTheTurnTests : IDisposable
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private readonly string _folder = IoPath.Combine(IoPath.GetTempPath(), "adp-delete-turn-" + Guid.NewGuid().ToString("N"));

    public AdpFileWriterDeleteTakesTheTurnTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        TestFolder.TryDelete(_folder);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task ADeleteWaitsForASaveOfTheSamePath_RatherThanCuttingIntoIt()
    {
        // Deterministic rather than raced: the save stops INSIDE its replace, holding the turn, and
        // the delete must still be waiting when it does. Timing decides nothing.
        var path = IoPath.Combine(_folder, "tea.owm");
        await File.WriteAllTextAsync(path, "before", TestContext.Current.CancellationToken);
        using var logs = LogCapture.Start();
        using var inside = new ManualResetEventSlim(false);
        using var release = new ManualResetEventSlim(false);

        var saving = Task.Run(() => AdpFileWriter.Save(path, "after", replace: (temporary, destination) =>
        {
            // ReSharper disable AccessToDisposedClosure - Reason: this replace callback runs inside `saving`, which the test awaits with saving.WaitAsync(Patience, ...) after release.Set() and before the usings dispose the two events.
            inside.Set();
            release.Wait(Patience, TestContext.Current.CancellationToken);
            // ReSharper restore AccessToDisposedClosure
            File.Replace(temporary, destination, destinationBackupFileName: null, ignoreMetadataErrors: true);
        }), TestContext.Current.CancellationToken);

        Assert.True(inside.Wait(Patience, TestContext.Current.CancellationToken), "The arrangement failed: the save never reached its replace.");
        var deleting = Task.Run(() => AdpFileWriter.Delete(path), TestContext.Current.CancellationToken);

        // The delete cannot have run: the save is holding the turn, mid-replace. Asked by racing
        // it against a delay rather than by blocking on it - the assertion is the same, and a
        // guard that blocks a test thread to measure blocking is one deadlock from silence.
        await Task.WhenAny(deleting, Task.Delay(TimeSpan.FromMilliseconds(500), TestContext.Current.CancellationToken));
        Assert.False(deleting.IsCompleted, "The delete cut into a save of the same path.");

        release.Set();
        await saving.WaitAsync(Patience, TestContext.Current.CancellationToken);
        Assert.Contains(logs.Warnings, warning => warning.Contains("Waited for a save of", StringComparison.Ordinal) && warning.Contains(path, StringComparison.Ordinal));
        await deleting.WaitAsync(Patience, TestContext.Current.CancellationToken);
        Assert.False(File.Exists(path), "The delete was serialised but never happened.");
    }

    [Fact]
    public void ADelete_IsRecordedWithThePathAndTheProcess()
    {
        // The correlation half: a completed delete holds nothing, so the holder query on a later
        // failed publish will find nobody. This line is what that failure is read against.
        var path = IoPath.Combine(_folder, "tea.owm");
        File.WriteAllText(path, "before");
        using var logs = LogCapture.Start();

        AdpFileWriter.Delete(path);

        Assert.Contains(
            logs.Informations,
            line => line.Contains(path, StringComparison.Ordinal) &&
                line.Contains($"pid {Environment.ProcessId}", StringComparison.Ordinal));
    }

    [Fact]
    public void DeletingWhatIsNotThere_IsNeitherAnErrorNorARecord()
    {
        // The must-not-catch half: File.Delete's own contract is that a missing file is success,
        // and a record of a delete that did not happen would be a false correlation later.
        var path = IoPath.Combine(_folder, "never-existed.owm");
        using var logs = LogCapture.Start();

        AdpFileWriter.Delete(path);

        Assert.DoesNotContain(logs.Informations, line => line.Contains(path, StringComparison.Ordinal));
    }
}
