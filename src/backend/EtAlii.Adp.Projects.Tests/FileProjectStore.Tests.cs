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
        var store = new FileProjectStore(_appDataRoot);

        // Assert.
        Assert.Empty(store.List(UserId));
    }

    [Fact]
    public void Add_WithValidFolder_PersistsAndReturnsRecord()
    {
        // Arrange.
        var store = new FileProjectStore(_appDataRoot);
        var folderPath = CreateRealFolder("my-project");

        // Act.
        var added = store.Add(UserId, "", ToPathRecord(folderPath));

        // Assert.
        Assert.Equal("my-project", added.Name);
        Assert.Single(store.List(UserId));
    }

    [Fact]
    public void Add_WithExplicitName_UsesItInsteadOfTheFolderName()
    {
        // Arrange.
        var store = new FileProjectStore(_appDataRoot);
        var folderPath = CreateRealFolder("my-project");

        // Act.
        var added = store.Add(UserId, "Custom Name", ToPathRecord(folderPath));

        // Assert.
        Assert.Equal("Custom Name", added.Name);
    }

    [Fact]
    public void Add_WithInvalidFolder_ThrowsAndDoesNotCorruptExistingList()
    {
        // Arrange.
        var store = new FileProjectStore(_appDataRoot);
        var validFolder = CreateRealFolder("existing-project");
        store.Add(UserId, "", ToPathRecord(validFolder));

        // Act.
        var missingPath = new PathRecord(new[] { _appDataRoot, "does-not-exist" });
        Assert.Throws<InvalidProjectPathException>(() => store.Add(UserId, "", missingPath));

        // Assert.
        Assert.Single(store.List(UserId));
    }

    [Fact]
    public void Remove_RemovesOnlyTheListEntry_NotTheFolder()
    {
        // Arrange.
        var store = new FileProjectStore(_appDataRoot);
        var folderPath = CreateRealFolder("to-remove");
        var added = store.Add(UserId, "", ToPathRecord(folderPath));

        // Act.
        store.Remove(UserId, added.Id);

        // Assert.
        Assert.Empty(store.List(UserId));
        Assert.True(Directory.Exists(folderPath));
    }

    [Fact]
    public void List_AfterReconstructingStore_StillReflectsPersistedProjects()
    {
        // Arrange.
        var store = new FileProjectStore(_appDataRoot);
        var folderPath = CreateRealFolder("survives-restart");
        store.Add(UserId, "", ToPathRecord(folderPath));

        // Act.
        // Simulate a process restart: a brand-new store instance re-reading the same file.
        var reloadedStore = new FileProjectStore(_appDataRoot);

        // Assert.
        Assert.Single(reloadedStore.List(UserId));
    }

    [Fact]
    public void List_AfterReconstructingStore_RoundTripsIdNameAndPathSegmentsExactly()
    {
        // Arrange.
        var store = new FileProjectStore(_appDataRoot);
        var folderPath = CreateRealFolder("round-trips-exactly");
        var path = ToPathRecord(folderPath);
        var added = store.Add(UserId, "Round Trip Name", path);

        // Act.
        var reloadedStore = new FileProjectStore(_appDataRoot);
        var reloaded = Assert.Single(reloadedStore.List(UserId));

        // Assert.
        Assert.Equal(added.Id, reloaded.Id);
        Assert.Equal("Round Trip Name", reloaded.Name);
        Assert.Equal(path.Segments, reloaded.Path.Segments);
    }

    [Fact]
    public void TwoConcurrentAdds_BothAreKept()
    {
        // Arrange. Rounds of two adds released together: without the lock both read the same list
        // and the second write drops the first one's project.
        var store = new FileProjectStore(_appDataRoot);
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
                barrier.SignalAndWait();
                store.Add(UserId, "", folder);
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
        var kept = new FileProjectStore(_appDataRoot).List(UserId);
        Assert.True(kept.Count == rounds * 2, $"{rounds * 2 - kept.Count} of {rounds * 2} concurrently added projects were lost.");
    }

    [Fact]
    public void List_WhileTheFileIsUnchanged_DoesNotReadItAgain()
    {
        // Arrange. The list is read once; then the file is overwritten with unreadable bytes of
        // the same size and given back its time, so only a read that skips the file succeeds.
        var store = new FileProjectStore(_appDataRoot);
        store.Add(UserId, "", ToPathRecord(CreateRealFolder("kept")));
        Assert.Single(store.List(UserId));
        var file = new FileInfo(StorePath());
        var written = file.LastWriteTimeUtc;
        File.WriteAllText(file.FullName, new string('x', (int)file.Length));
        File.SetLastWriteTimeUtc(file.FullName, written);

        // Act.
        var listed = store.List(UserId);

        // Assert.
        Assert.Single(listed);
    }

    [Fact]
    public void List_AfterTheFileIsChangedOutsideTheStore_ReadsItAgain()
    {
        // Arrange.
        var store = new FileProjectStore(_appDataRoot);
        store.Add(UserId, "", ToPathRecord(CreateRealFolder("first")));
        Assert.Single(store.List(UserId));

        // Act. Another process empties the list.
        File.WriteAllText(StorePath(), "[]");
        var listed = store.List(UserId);

        // Assert.
        Assert.Empty(listed);
    }

    private string StorePath() =>
        IoPath.Combine(_appDataRoot, "EtAlii.Adp", "users", UserId.ToString(), "projects.json");
}
