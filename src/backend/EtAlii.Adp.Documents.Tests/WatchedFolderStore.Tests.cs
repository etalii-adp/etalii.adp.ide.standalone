using Xunit;

namespace EtAlii.Adp.Documents.Tests;

/// <summary>
/// The watched-folder store's contract over the smallest snapshot there is: the names and texts of
/// a folder's files (code-structure FR-037).
/// </summary>
public sealed class WatchedFolderStoreTests : WatchedFolderStoreContract<WatchedFolderStoreTests.Listing>
{
    protected override WatchedFolderStore<Listing> CreateStore(TimeSpan settleDelay) => new ListingStore(settleDelay);

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
        var listing = await heard.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);
        Assert.Contains(Path.Combine("deeper", "b.txt"), listing.Files.Keys);
    }

    /// <summary>A folder's files, by path relative to it, with their text.</summary>
    public sealed record Listing(IReadOnlyDictionary<string, string> Files);

    private sealed class ListingStore(TimeSpan settleDelay) : WatchedFolderStore<Listing>(settleDelay)
    {
        protected override Listing Read(string folder) => new(
            Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
                .ToDictionary(path => Path.GetRelativePath(folder, path), SharedDocumentReader.ReadAllText, StringComparer.Ordinal));
    }
}
