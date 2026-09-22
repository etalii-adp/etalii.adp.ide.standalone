using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Documents.Tests;

/// <summary>
/// Concurrent in-process saves to one destination are serialised, and a save that had to wait says
/// so - the remedy for the <c>DiagramElementActionFlowTests</c> flake.
/// </summary>
/// <remarks>
/// <para>
/// <b>The mechanism, measured on 2026-09-15.</b> Two threads calling the real <c>Save</c> on one
/// path failed 1009 of 3000 saves with <c>IOException 0x80070497</c> (1175) <c>"Unable to remove
/// the file to be replaced."</c> - the exact wild exception from five captured reds - plus
/// <c>FileNotFoundException</c> and 1177 ("renamed using the backup name"), meaning the destination
/// could be briefly missing or misnamed mid-race. Readers never produced it, in any sharing mode.
/// </para>
/// <para>
/// <b>Why the wait is logged, not only prevented.</b> In the failing test the two actions are awaited
/// in turn, so a second writer to the same document exists and is not yet named. A silent lock would
/// hide it; a logged wait names it by path on every occurrence without failing anything.
/// </para>
/// </remarks>
public class AdpFileWriterConcurrentSavesTests : IDisposable
{
    private const string WaitedMessage = "Waited for another save to";
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private readonly string _folder = IoPath.Combine(IoPath.GetTempPath(), "adp-concurrent-saves-" + Guid.NewGuid().ToString("N"));

    public AdpFileWriterConcurrentSavesTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        TestFolder.TryDelete(_folder);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void ConcurrentSavesToOnePath_AllSucceed_AndLeaveExactlyOneWritersCompleteContent()
    {
        // Arrange. Real Save, real File.Replace, no seam - the measured race, flat out.
        var path = IoPath.Combine(_folder, "roadmap.mm");
        File.WriteAllText(path, "before");
        const int writers = 4;
        const int savesEach = 300;
        var failures = new System.Collections.Concurrent.ConcurrentQueue<string>();
        var written = new System.Collections.Concurrent.ConcurrentDictionary<string, byte>();
        using var go = new ManualResetEventSlim(false);

        // Act.
        var threads = Enumerable.Range(0, writers).Select(w => new Thread(() =>
        {
            go.Wait();
            for (var i = 0; i < savesEach; i++)
            {
                // Long enough that a torn or interleaved write could not pass as a real one.
                var content = $"writer {w} save {i} " + new string((char)('a' + w), 4096);
                written[content] = 0;
                try
                {
                    AdpFileWriter.Save(path, content);
                }
                catch (Exception e)
                {
                    failures.Enqueue($"{e.GetType().Name} 0x{e.HResult:X8} {e.Message}");
                }
            }
        })).ToArray();
        foreach (var thread in threads)
        {
            thread.Start();
        }
        go.Set();
        foreach (var thread in threads)
        {
            thread.Join();
        }

        // Assert.
        Assert.True(failures.IsEmpty, $"{failures.Count} of {writers * savesEach} concurrent saves failed, first: {failures.FirstOrDefault()}");
        Assert.Contains(File.ReadAllText(path), written.Keys);
        Assert.Empty(Directory.GetFiles(_folder, $"{AdpFileWriter.TempPrefix}*"));
    }

    [Fact]
    public void ASaveThatWaitsForAnotherSaveToTheSameDestination_LogsTheWaitNamingThePath()
    {
        // Arrange. The first save stops inside its replace - holding whatever serialises saves -
        // until released. Deterministic: no timing decides whether the second one contends.
        var path = IoPath.Combine(_folder, "roadmap.mm");
        File.WriteAllText(path, "before");
        using var logs = LogCapture.Start();
        using var firstIsInside = new ManualResetEventSlim(false);
        using var releaseFirst = new ManualResetEventSlim(false);

        var first = Task.Run(() => AdpFileWriter.Save(path, "first", replace: (temporary, destination) =>
        {
            firstIsInside.Set();
            releaseFirst.Wait(Patience);
            File.Replace(temporary, destination, destinationBackupFileName: null, ignoreMetadataErrors: true);
        }), TestContext.Current.CancellationToken);
        Assert.True(firstIsInside.Wait(Patience), "The first save never reached its replace.");

        // Act. A second save to the same destination while the first is inside.
        var second = Task.Run(() => AdpFileWriter.Save(path, "second"), TestContext.Current.CancellationToken);

        // Assert, first: it waits - and says so, by path, before it gets its turn.
        Assert.True(
            SpinWait.SpinUntil(() => logs.Warnings.Any(w => w.Contains(WaitedMessage, StringComparison.Ordinal)), Patience),
            "The second save to the same destination did not log that it waited.");
        Assert.False(second.IsCompleted, "The second save completed while the first still held the destination.");
        Assert.Contains(logs.Warnings, w => w.Contains(WaitedMessage, StringComparison.Ordinal) && w.Contains(IoPath.GetFileName(path), StringComparison.Ordinal));

        // Then both finish, in order, and the later one's content is what remains.
        releaseFirst.Set();
        Assert.True(Task.WaitAll([first, second], Patience), "The saves did not finish after the first was released.");
        Assert.Equal("second", File.ReadAllText(path));
    }

    [Fact]
    public void TwoSpellingsOfOneDestination_ContendForTheSameTurn()
    {
        // Condition 1: the key is the normalised full path, compared ignoring case on Windows. Two
        // spellings taking two locks would race exactly as before the fix.
        var path = IoPath.Combine(_folder, "roadmap.mm");
        File.WriteAllText(path, "before");
        var otherSpelling = IoPath.Combine(_folder, ".", "ROADMAP.MM");
        using var logs = LogCapture.Start();
        using var firstIsInside = new ManualResetEventSlim(false);
        using var releaseFirst = new ManualResetEventSlim(false);

        var first = Task.Run(() => AdpFileWriter.Save(path, "first", replace: (temporary, destination) =>
        {
            firstIsInside.Set();
            releaseFirst.Wait(Patience);
            File.Replace(temporary, destination, destinationBackupFileName: null, ignoreMetadataErrors: true);
        }), TestContext.Current.CancellationToken);
        Assert.True(firstIsInside.Wait(Patience), "The first save never reached its replace.");

        var second = Task.Run(() => AdpFileWriter.Save(otherSpelling, "second"), TestContext.Current.CancellationToken);

        Assert.True(
            SpinWait.SpinUntil(() => logs.Warnings.Any(w => w.Contains(WaitedMessage, StringComparison.Ordinal)), Patience),
            "A different spelling of the same destination did not wait for the save already inside it.");
        Assert.False(second.IsCompleted, "A different spelling of the same destination got through while the first held it.");

        releaseFirst.Set();
        Assert.True(Task.WaitAll([first, second], Patience), "The saves did not finish after the first was released.");
    }

    [Fact]
    public void SavesToDifferentDestinations_DoNotWaitForEachOther()
    {
        // Condition 3, the must-not-catch half: serialising unrelated paths would be a slowdown
        // with a false wait logged against a file nobody else was writing.
        var held = IoPath.Combine(_folder, "roadmap.mm");
        var other = IoPath.Combine(_folder, "other.mm");
        File.WriteAllText(held, "before");
        File.WriteAllText(other, "before");
        using var logs = LogCapture.Start();
        using var firstIsInside = new ManualResetEventSlim(false);
        using var releaseFirst = new ManualResetEventSlim(false);

        var first = Task.Run(() => AdpFileWriter.Save(held, "first", replace: (temporary, destination) =>
        {
            firstIsInside.Set();
            releaseFirst.Wait(Patience);
            File.Replace(temporary, destination, destinationBackupFileName: null, ignoreMetadataErrors: true);
        }), TestContext.Current.CancellationToken);
        Assert.True(firstIsInside.Wait(Patience), "The first save never reached its replace.");

        try
        {
            // Act: a save to a different file completes while the first is still inside.
            AdpFileWriter.Save(other, "unrelated");

            // Assert.
            Assert.Equal("unrelated", File.ReadAllText(other));
            Assert.DoesNotContain(logs.Warnings, w => w.Contains(WaitedMessage, StringComparison.Ordinal));
        }
        finally
        {
            releaseFirst.Set();
            first.Wait(Patience, TestContext.Current.CancellationToken);
        }
    }
}
