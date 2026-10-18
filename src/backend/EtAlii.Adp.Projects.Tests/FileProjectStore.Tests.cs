using EtAlii.Adp.Files;
using Xunit;
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Projects.Tests;

public class FileProjectStoreTests : IDisposable
{
    private static readonly ShortGuid UserId = ShortGuid.FromName("user-1");

    private readonly string _appDataRoot;

    public FileProjectStoreTests()
    {
        _appDataRoot = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_appDataRoot);
    }

    public void Dispose()
    {
        TestFolder.TryDelete(_appDataRoot);
    }

    private string CreateRealFolder(string name)
    {
        var folderPath = IoPath.Combine(_appDataRoot, name);
        Directory.CreateDirectory(folderPath);
        return folderPath;
    }

    private static PathRecord ToPathRecord(string folderPath) =>
        new(folderPath.Split(IoPath.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries));

    [Fact]
    public void List_WithNoPersistedFile_ReturnsEmpty()
    {
        // Act.
        var store = new FileProjectStore(_appDataRoot, new FileWriter());

        // Assert.
        Assert.Empty(store.List(UserId));
    }

    [Fact]
    public async Task Add_WithValidFolder_PersistsAndReturnsRecord()
    {
        // Arrange.
        var store = new FileProjectStore(_appDataRoot, new FileWriter());
        var folderPath = CreateRealFolder("my-project");

        // Act.
        var added = await store.AddAsync(UserId, "", ToPathRecord(folderPath), TestContext.Current.CancellationToken);

        // Assert.
        Assert.Equal("my-project", added.Name);
        Assert.Single(store.List(UserId));
    }

    [Fact]
    public async Task Add_WithExplicitName_UsesItInsteadOfTheFolderName()
    {
        // Arrange.
        var store = new FileProjectStore(_appDataRoot, new FileWriter());
        var folderPath = CreateRealFolder("my-project");

        // Act.
        var added = await store.AddAsync(UserId, "Custom Name", ToPathRecord(folderPath), TestContext.Current.CancellationToken);

        // Assert.
        Assert.Equal("Custom Name", added.Name);
    }

    [Fact]
    public async Task Add_WithInvalidFolder_ThrowsAndDoesNotCorruptExistingList()
    {
        // Arrange.
        var store = new FileProjectStore(_appDataRoot, new FileWriter());
        var validFolder = CreateRealFolder("existing-project");
        await store.AddAsync(UserId, "", ToPathRecord(validFolder), TestContext.Current.CancellationToken);

        // Act.
        var missingPath = new PathRecord(new[] { _appDataRoot, "does-not-exist" });
        await Assert.ThrowsAsync<InvalidProjectPathException>(() => store.AddAsync(UserId, "", missingPath, TestContext.Current.CancellationToken));

        // Assert.
        Assert.Single(store.List(UserId));
    }

    [Fact]
    public async Task Remove_RemovesOnlyTheListEntry_NotTheFolder()
    {
        // Arrange.
        var store = new FileProjectStore(_appDataRoot, new FileWriter());
        var folderPath = CreateRealFolder("to-remove");
        var added = await store.AddAsync(UserId, "", ToPathRecord(folderPath), TestContext.Current.CancellationToken);

        // Act.
        await store.RemoveAsync(UserId, added.Id, TestContext.Current.CancellationToken);

        // Assert.
        Assert.Empty(store.List(UserId));
        Assert.True(Directory.Exists(folderPath));
    }

    [Fact]
    public async Task List_AfterReconstructingStore_StillReflectsPersistedProjects()
    {
        // Arrange.
        var store = new FileProjectStore(_appDataRoot, new FileWriter());
        var folderPath = CreateRealFolder("survives-restart");
        await store.AddAsync(UserId, "", ToPathRecord(folderPath), TestContext.Current.CancellationToken);

        // Act.
        // Simulate a process restart: a brand-new store instance re-reading the same file.
        var reloadedStore = new FileProjectStore(_appDataRoot, new FileWriter());

        // Assert.
        Assert.Single(reloadedStore.List(UserId));
    }

    [Fact]
    public async Task List_AfterReconstructingStore_RoundTripsIdNameAndPathSegmentsExactly()
    {
        // Arrange.
        var store = new FileProjectStore(_appDataRoot, new FileWriter());
        var folderPath = CreateRealFolder("round-trips-exactly");
        var path = ToPathRecord(folderPath);
        var added = await store.AddAsync(UserId, "Round Trip Name", path, TestContext.Current.CancellationToken);

        // Act.
        var reloadedStore = new FileProjectStore(_appDataRoot, new FileWriter());
        var reloaded = Assert.Single(reloadedStore.List(UserId));

        // Assert.
        Assert.Equal(added.Id, reloaded.Id);
        Assert.Equal("Round Trip Name", reloaded.Name);
        Assert.Equal(path.Segments, reloaded.Path.Segments);
    }

    [Fact]
    public void TwoConcurrentAdds_BothAreKept()
    {
        // Arrange. Rounds of two adds released together: without the turn both read the same list
        // and the second write drops the first one's project.
        var store = new FileProjectStore(_appDataRoot, new FileWriter());
        var cancellationToken = TestContext.Current.CancellationToken;
        const int rounds = 25;
        var folders = Enumerable.Range(0, rounds * 2).Select(i => ToPathRecord(CreateRealFolder($"concurrent-{i}"))).ToArray();

        // Act.
        for (var round = 0; round < rounds; round++)
        {
            using var barrier = new Barrier(2);
            var pair = new[] { folders[round * 2], folders[round * 2 + 1] };
            var workers = pair.Select(folder => new Thread(() =>
            {
                // ReSharper disable once AccessToDisposedClosure - Reason: both worker threads that run this closure are Joined below before the using disposes `barrier`.
                barrier.SignalAndWait(cancellationToken);
                store.AddAsync(UserId, "", folder, cancellationToken).GetAwaiter().GetResult();
            })).ToArray();
            foreach (var worker in workers)
            {
                worker.Start();
            }
            foreach (var worker in workers)
            {
                worker.Join();
            }
        }

        // Assert. Read back by a store that keeps nothing from the adds.
        var kept = new FileProjectStore(_appDataRoot, new FileWriter()).List(UserId);
        Assert.True(kept.Count == rounds * 2, $"{rounds * 2 - kept.Count} of {rounds * 2} concurrently added projects were lost.");
    }

    [Fact]
    public async Task List_WhileTheFileIsUnchanged_DoesNotReadItAgain()
    {
        // Arrange. The list is read once; then the file is overwritten with unreadable bytes of
        // the same size and given back its time, so only a read that skips the file succeeds.
        var store = new FileProjectStore(_appDataRoot, new FileWriter());
        await store.AddAsync(UserId, "", ToPathRecord(CreateRealFolder("kept")), TestContext.Current.CancellationToken);
        Assert.Single(store.List(UserId));
        var file = new FileInfo(StorePath());
        var written = file.LastWriteTimeUtc;
        await File.WriteAllTextAsync(file.FullName, new string('x', (int)file.Length), TestContext.Current.CancellationToken);
        File.SetLastWriteTimeUtc(file.FullName, written);

        // Act.
        var listed = store.List(UserId);

        // Assert.
        Assert.Single(listed);
    }

    [Fact]
    public async Task List_AfterTheFileIsChangedOutsideTheStore_ReadsItAgain()
    {
        // Arrange.
        var store = new FileProjectStore(_appDataRoot, new FileWriter());
        await store.AddAsync(UserId, "", ToPathRecord(CreateRealFolder("first")), TestContext.Current.CancellationToken);
        Assert.Single(store.List(UserId));

        // Act. Another process empties the list.
        await File.WriteAllTextAsync(StorePath(), "[]", TestContext.Current.CancellationToken);
        var listed = store.List(UserId);

        // Assert.
        Assert.Empty(listed);
    }

    [Fact]
    public async Task Add_PublishesTheListThroughTheWriterItIsHanded()
    {
        // Arrange. The host hands one writer to everything that saves, so saves of one path take turns.
        var writer = new RecordingWriter();
        var store = new FileProjectStore(_appDataRoot, writer);

        // Act.
        await store.AddAsync(UserId, "Handed", ToPathRecord(CreateRealFolder("handed")), TestContext.Current.CancellationToken);

        // Assert.
        (string path, string content) = Assert.Single(writer.Saved);
        Assert.Equal(StorePath(), path);
        Assert.Contains("Handed", content, StringComparison.Ordinal);
    }

    private string StorePath() =>
        IoPath.Combine(_appDataRoot, "EtAlii.Adp", "users", UserId.ToString(), "projects.json");

    /// <summary>Records each save it is handed and writes nothing.</summary>
    private sealed class RecordingWriter : IFileWriter
    {
        public List<(string Path, string Content)> Saved { get; } = [];

        public Task SaveAsync(string path, string content, CancellationToken cancellationToken)
        {
            Saved.Add((path, content));
            return Task.CompletedTask;
        }

        public Task SaveAsync(string path, byte[] content, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The store saves its list as text.");

        public Task DeleteAsync(string path, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The store deletes nothing.");
    }
}
