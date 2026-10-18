using EtAlii.Adp.Files;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Documents.Tests;

/// <summary>
/// The injectable writer's turns: one per destination while a save or a delete holds or waits for
/// it, and none after. The static writer kept a turn for every path it ever saved, for the life of
/// the process (code-structure FR-068).
/// </summary>
public class FileWriterTurnsTests : IDisposable
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private readonly string _folder = IoPath.Combine(IoPath.GetTempPath(), "adp-file-writer-turns-" + Guid.NewGuid().ToString("N"));

    public FileWriterTurnsTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        TestFolder.TryDelete(_folder);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task WritesToManyPaths_LeaveNoTurnHeld()
    {
        // Arrange.
        var writer = new FileWriter();
        var paths = Enumerable.Range(0, 200).Select(index => IoPath.Combine(_folder, $"document-{index}.txt")).ToArray();

        // Act: every path saved, half of them twice at once, and every other one deleted.
        await Task.WhenAll(paths.Select(path => writer.SaveAsync(path, "first", TestContext.Current.CancellationToken)));
        await Task.WhenAll(paths.Take(100).SelectMany(path => new[]
        {
            writer.SaveAsync(path, "second", TestContext.Current.CancellationToken),
            writer.SaveAsync(path, "third"u8.ToArray(), TestContext.Current.CancellationToken),
        }));
        await Task.WhenAll(paths.Where((_, index) => index % 2 == 0).Select(path => writer.DeleteAsync(path, TestContext.Current.CancellationToken)));

        // Assert.
        Assert.Equal(0, writer.TurnsHeld);
        Assert.Equal(100, Directory.GetFiles(_folder).Length);
    }

    [Fact]
    public async Task ASaveOfABusyPath_WaitsWithoutBlocking_ThenPublishesAfterIt()
    {
        // Arrange: the first save stops inside its replace, holding the path's turn.
        var writer = new FileWriter();
        var path = IoPath.Combine(_folder, "roadmap.mm");
        await File.WriteAllTextAsync(path, "before", TestContext.Current.CancellationToken);
        using var firstIsInside = new ManualResetEventSlim(false);
        using var releaseFirst = new ManualResetEventSlim(false);
        var first = Task.Run(
            () => writer.SaveAsync(path, "first", null, (temporary, destination) =>
            {
                // ReSharper disable AccessToDisposedClosure - Reason: the test awaits `first` before the usings dispose the two events.
                firstIsInside.Set();
                releaseFirst.Wait(Patience, TestContext.Current.CancellationToken);
                // ReSharper restore AccessToDisposedClosure
                File.Replace(temporary, destination, destinationBackupFileName: null, ignoreMetadataErrors: true);
            }, TestContext.Current.CancellationToken),
            TestContext.Current.CancellationToken);
        Assert.True(firstIsInside.Wait(Patience, TestContext.Current.CancellationToken), "The first save never reached its replace.");

        // Act: called on this thread, the second save returns while the first still holds the turn.
        var second = writer.SaveAsync(path, "second", TestContext.Current.CancellationToken);

        // Assert.
        Assert.False(second.IsCompleted, "The second save completed while the first still held the destination.");
        Assert.Equal(1, writer.TurnsHeld);
        releaseFirst.Set();
        await first;
        await second;
        Assert.Equal("second", await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
        Assert.Equal(0, writer.TurnsHeld);
    }

    [Fact]
    public async Task AWaitThatIsCancelled_LeavesNoTurnBehind()
    {
        // Arrange.
        var writer = new FileWriter();
        var path = IoPath.Combine(_folder, "roadmap.mm");
        await File.WriteAllTextAsync(path, "before", TestContext.Current.CancellationToken);
        using var firstIsInside = new ManualResetEventSlim(false);
        using var releaseFirst = new ManualResetEventSlim(false);
        var first = Task.Run(
            () => writer.SaveAsync(path, "first", null, (temporary, destination) =>
            {
                // ReSharper disable AccessToDisposedClosure - Reason: the test awaits `first` before the usings dispose the two events.
                firstIsInside.Set();
                releaseFirst.Wait(Patience, TestContext.Current.CancellationToken);
                // ReSharper restore AccessToDisposedClosure
                File.Replace(temporary, destination, destinationBackupFileName: null, ignoreMetadataErrors: true);
            }, TestContext.Current.CancellationToken),
            TestContext.Current.CancellationToken);
        Assert.True(firstIsInside.Wait(Patience, TestContext.Current.CancellationToken), "The first save never reached its replace.");
        using var giveUp = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

        // Act.
        var waiting = writer.DeleteAsync(path, giveUp.Token);
        await giveUp.CancelAsync();

        // Assert.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
        releaseFirst.Set();
        await first;
        Assert.Equal(0, writer.TurnsHeld);
        Assert.True(File.Exists(path), "The cancelled delete ran after all.");
    }
}
