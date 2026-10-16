using Xunit;

namespace EtAlii.Adp.Documents.Tests;

/// <summary>
/// The watched-folder store's contract over the smallest snapshot there is: the names and texts of
/// a folder's files (code-structure FR-037).
/// </summary>
public sealed class WatchedFolderStoreTests : WatchedFolderStoreContract<WatchedFolderStoreTests.Listing>
{
    protected override WatchedFolderStore<Listing> CreateStore(TimeSpan settleDelay, TimeProvider time) => new ListingStore(settleDelay, time);

    protected override void Populate(string root) => File.WriteAllText(Path.Combine(root, "a.txt"), "first");

    protected override Task ChangeAsync(string root, CancellationToken cancellationToken) =>
        File.WriteAllTextAsync(Path.Combine(root, "a.txt"), "changed", cancellationToken);

    protected override bool ShowsTheChange(Listing snapshot) => snapshot.Files["a.txt"] == "changed";

    protected override Task TouchAsync(string root, int index, CancellationToken cancellationToken) =>
        File.AppendAllTextAsync(Path.Combine(root, "a.txt"), $" touch {index}", cancellationToken);

    [Fact]
    public async Task AFileAddedInASubfolder_IsPickedUp()
    {
        // Arrange.
        var cancellationToken = TestContext.Current.CancellationToken;
        Store.GetOrLoad(Root);
        var heard = new TaskCompletionSource<Listing>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var watch = Store.Watch(Root, snapshot => heard.TrySetResult(snapshot));

        // Act.
        Directory.CreateDirectory(Path.Combine(Root, "deeper"));
        await File.WriteAllTextAsync(Path.Combine(Root, "deeper", "b.txt"), "below", cancellationToken);

        // Assert: subfolders are watched too.
        Assert.True(await SettlesAsync(() => heard.Task.IsCompleted, cancellationToken), "A file added in a subfolder was not heard.");
        Assert.Contains(Path.Combine("deeper", "b.txt"), (await heard.Task).Files.Keys);
    }

    [Fact]
    public async Task AChange_SettlesOnTheStoresClock_NotOnTheWallClock()
    {
        // Arrange.
        var cancellationToken = TestContext.Current.CancellationToken;
        Store.GetOrLoad(Root);
        var heard = 0;
        using var watch = Store.Watch(Root, _ => Interlocked.Increment(ref heard));

        // Act: changed, and real time let pass well beyond the settle delay with the clock still.
        await ChangeAsync(Root, cancellationToken);
        var early = await Eventually.TrueAsync(() => Volatile.Read(ref heard) > 0, cancellationToken, Silence);

        // Assert: nothing settled until the clock moved, and then it did (code-structure FR-051).
        Assert.False(early, "The change settled on the wall clock rather than on the store's.");
        Assert.True(await SettlesAsync(() => Volatile.Read(ref heard) > 0, cancellationToken), "The change did not settle once the clock moved.");
    }

    /// <summary>A folder's files, by path relative to it, with their text.</summary>
    public sealed record Listing(IReadOnlyDictionary<string, string> Files);

    private sealed class ListingStore(TimeSpan settleDelay, TimeProvider time) : WatchedFolderStore<Listing>(settleDelay, time: time)
    {
        protected override Listing Read(string folder) => new(
            Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
                .ToDictionary(path => Path.GetRelativePath(folder, path), SharedDocumentReader.ReadAllText, StringComparer.Ordinal));
    }
}
