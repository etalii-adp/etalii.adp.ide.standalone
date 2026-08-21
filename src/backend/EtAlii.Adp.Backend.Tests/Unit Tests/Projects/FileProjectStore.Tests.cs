using EtAlii.Adp.Backend.Projects;
using Xunit;
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Backend.Tests;

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
        if (Directory.Exists(_appDataRoot))
        {
            Directory.Delete(_appDataRoot, recursive: true);
        }
    }

    private string CreateRealFolder(string name)
    {
        var folderPath = IoPath.Combine(_appDataRoot, name);
        Directory.CreateDirectory(folderPath);
        return folderPath;
    }

    [Fact]
    public void List_WithNoPersistedFile_ReturnsEmpty()
    {
        var store = new FileProjectStore(_appDataRoot);

        Assert.Empty(store.List(UserId));
    }

    [Fact]
    public void Add_WithValidFolder_PersistsAndReturnsRecord()
    {
        var store = new FileProjectStore(_appDataRoot);
        var folderPath = CreateRealFolder("my-project");
        var segments = folderPath.Split(IoPath.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);

        var added = store.Add(UserId, "", segments);

        Assert.Equal("my-project", added.Name);
        Assert.Single(store.List(UserId));
    }

    [Fact]
    public void Add_WithExplicitName_UsesItInsteadOfTheFolderName()
    {
        var store = new FileProjectStore(_appDataRoot);
        var folderPath = CreateRealFolder("my-project");
        var segments = folderPath.Split(IoPath.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);

        var added = store.Add(UserId, "Custom Name", segments);

        Assert.Equal("Custom Name", added.Name);
    }

    [Fact]
    public void Add_WithInvalidFolder_ThrowsAndDoesNotCorruptExistingList()
    {
        var store = new FileProjectStore(_appDataRoot);
        var validFolder = CreateRealFolder("existing-project");
        store.Add(UserId, "", validFolder.Split(IoPath.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries));

        var missingSegments = new[] { _appDataRoot, "does-not-exist" };
        Assert.Throws<InvalidProjectPathException>(() => store.Add(UserId, "", missingSegments));

        Assert.Single(store.List(UserId));
    }

    [Fact]
    public void Remove_RemovesOnlyTheListEntry_NotTheFolder()
    {
        var store = new FileProjectStore(_appDataRoot);
        var folderPath = CreateRealFolder("to-remove");
        var added = store.Add(UserId, "", folderPath.Split(IoPath.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries));

        store.Remove(UserId, added.Id);

        Assert.Empty(store.List(UserId));
        Assert.True(Directory.Exists(folderPath));
    }

    [Fact]
    public void List_AfterReconstructingStore_StillReflectsPersistedProjects()
    {
        var store = new FileProjectStore(_appDataRoot);
        var folderPath = CreateRealFolder("survives-restart");
        store.Add(UserId, "", folderPath.Split(IoPath.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries));

        // Simulate a process restart: a brand-new store instance re-reading the same file.
        var reloadedStore = new FileProjectStore(_appDataRoot);

        Assert.Single(reloadedStore.List(UserId));
    }
}
