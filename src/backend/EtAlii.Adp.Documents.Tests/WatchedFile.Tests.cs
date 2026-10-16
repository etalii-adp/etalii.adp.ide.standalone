using Microsoft.Extensions.Time.Testing;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Documents.Tests;

/// <summary>
/// The watched file: a read retried on a clock rather than a sleeping thread, and re-reads asked for
/// by events that run one at a time and never one too few (code-structure T145, FR-050).
/// </summary>
/// <remarks>
/// <b>The attempt count is the assertion, not the outcome.</b> A retry that gave up at once and one
/// that gave up after three give the identical refusal, so only the count tells them apart.
/// </remarks>
public sealed class WatchedFileTests : IDisposable
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private readonly string _root = Directory.CreateTempSubdirectory("adp-watched-file-").FullName;
    private readonly FakeTimeProvider _clock = new();

    public void Dispose() => TestFolder.TryDelete(_root);

    // ---- the read-retry --------------------------------------------------------------------------

    [Fact]
    public async Task ARefusedRead_IsTriedAgain_OnlyOnceTheClockHasMoved()
    {
        // Arrange: a read refused twice, as a file mid-replace is.
        using var file = new WatchedFile(Path("notes.txt"), () => Task.CompletedTask, _clock);
        var reads = 0;

        // Act.
        var reading = file.ReadAsync(_ => ++reads > 2 ? "opened" : null, read => read is not null, TestContext.Current.CancellationToken);

        // Assert: one attempt, then a wait on the clock rather than on a thread. Real time passing
        // changes nothing while the clock stands still; a wait that did not use the clock would have
        // run its attempts by now.
        Assert.False(await Eventually.TrueAsync(() => Volatile.Read(ref reads) > 1, TestContext.Current.CancellationToken, TimeSpan.FromMilliseconds(200)), "A refused read was tried again in real time.");
        Assert.False(reading.IsCompleted);

        await _clock.DriveAsync(reading, WatchedFile.BetweenReadAttempts);
        (string? result, int attempts) = await reading;
        Assert.Equal("opened", result);
        Assert.Equal(3, attempts);
        Assert.Equal(3, reads);
    }

    [Fact]
    public async Task AReadRefusedEveryTime_IsGivenUp_AfterTheAttempts()
    {
        using var file = new WatchedFile(Path("notes.txt"), () => Task.CompletedTask, _clock);
        var reads = 0;

        var reading = file.ReadAsync<string?>(_ => { reads++; return null; }, read => read is not null, TestContext.Current.CancellationToken);
        await _clock.DriveAsync(reading, WatchedFile.BetweenReadAttempts);

        (string? result, int attempts) = await reading;
        Assert.Null(result);
        Assert.Equal(WatchedFile.ReadAttempts, attempts);
        Assert.Equal(WatchedFile.ReadAttempts, reads);
    }

    [Fact]
    public async Task AReadThatOpens_IsNotTriedAgain()
    {
        using var file = new WatchedFile(Path("notes.txt"), () => Task.CompletedTask, _clock);
        var reads = 0;

        var reading = file.ReadAsync(path => { reads++; return path; }, _ => true, TestContext.Current.CancellationToken);

        // Finished without the clock moving at all.
        Assert.True(reading.IsCompleted);
        (string result, int attempts) = await reading;
        Assert.Equal(Path("notes.txt"), result);
        Assert.Equal(1, attempts);
        Assert.Equal(1, reads);
    }

    // ---- the re-reads events ask for -------------------------------------------------------------

    [Fact]
    public async Task ReReads_RunOneAtATime_AndOneMoreForWhatArrivedMeanwhile()
    {
        // Arrange: a re-read that holds until the test lets it go.
        var running = 0;
        var most = 0;
        var runs = 0;
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var file = new WatchedFile(Path("notes.txt"), async () =>
        {
            var now = Interlocked.Increment(ref running);
            most = Math.Max(most, now);
            Interlocked.Increment(ref runs);
            started.TrySetResult();
            await release.Task;
            Interlocked.Decrement(ref running);
        }, _clock);

        // Act: one re-read starts; three more events arrive while it runs.
        var first = file.Notify();
        await started.Task.WaitAsync(Patience, TestContext.Current.CancellationToken);
        var second = file.Notify();
        var third = file.Notify();
        var fourth = file.Notify();
        release.SetResult();
        await Task.WhenAll(first, second, third, fourth).WaitAsync(Patience, TestContext.Current.CancellationToken);

        // Assert: never two at once; the three that arrived meanwhile are one more re-read, not three and not none.
        Assert.Equal(1, most);
        Assert.Equal(2, runs);
        Assert.Same(second, third);
        Assert.Same(second, fourth);
    }

    [Fact]
    public async Task AReReadThatThrows_DoesNotStopTheNext()
    {
        var runs = 0;
        using var file = new WatchedFile(Path("notes.txt"), () =>
        {
            if (Interlocked.Increment(ref runs) == 1)
            {
                throw new IOException("The first re-read fails.");
            }

            return Task.CompletedTask;
        }, _clock);

        await file.Notify().WaitAsync(Patience, TestContext.Current.CancellationToken);
        await file.Notify().WaitAsync(Patience, TestContext.Current.CancellationToken);

        Assert.Equal(2, runs);
    }

    [Fact]
    public async Task ADisposedFile_ReadsNothingAgain()
    {
        var runs = 0;
        var file = new WatchedFile(Path("notes.txt"), () =>
        {
            Interlocked.Increment(ref runs);
            return Task.CompletedTask;
        }, _clock);
        file.Dispose();

        await file.Notify().WaitAsync(Patience, TestContext.Current.CancellationToken);

        Assert.Equal(0, runs);
    }

    [Fact]
    public async Task AWriteByAnotherProgram_AsksForAReRead()
    {
        // Arrange.
        var path = Path("notes.txt");
        await File.WriteAllTextAsync(path, "before", TestContext.Current.CancellationToken);
        var runs = 0;
        using var file = new WatchedFile(path, () =>
        {
            Interlocked.Increment(ref runs);
            return Task.CompletedTask;
        }, _clock);

        // Act.
        await File.WriteAllTextAsync(path, "after", TestContext.Current.CancellationToken);

        // Assert: heard through the real watcher.
        Assert.True(await Eventually.TrueAsync(() => Volatile.Read(ref runs) > 0, TestContext.Current.CancellationToken, Patience), "The write was never heard.");
    }

    [Fact]
    public async Task AnOverflow_AsksForAReRead()
    {
        // Arrange: nothing is heard while the watcher is off, so only the overflow can ask.
        var path = Path("notes.txt");
        await File.WriteAllTextAsync(path, "before", TestContext.Current.CancellationToken);
        var runs = 0;
        using var file = new WatchedFile(path, () =>
        {
            Interlocked.Increment(ref runs);
            return Task.CompletedTask;
        }, _clock);
        file.Watcher().EnableRaisingEvents = false;
        await File.WriteAllTextAsync(path, "after", TestContext.Current.CancellationToken);

        // Act.
        file.Watcher().RaiseOverflow();

        // Assert.
        Assert.True(await Eventually.TrueAsync(() => Volatile.Read(ref runs) > 0, TestContext.Current.CancellationToken, Patience), "The overflow never asked for a re-read.");
    }

    [Fact]
    public void AFileInAFolderThatDoesNotExist_IsReadAndNotWatched()
    {
        using var file = new WatchedFile(IoPath.Combine(_root, "missing", "notes.txt"), () => Task.CompletedTask, _clock);

        Assert.Null(file.Folder);
    }

    private string Path(string name) => IoPath.Combine(_root, name);
}
