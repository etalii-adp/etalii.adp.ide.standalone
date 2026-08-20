using EtAlii.Adp.Backend.Projects;
using Xunit;
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Backend.Tests.Projects;

public class FileProjectStoreTests : IDisposable
{
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

        Assert.Empty(store.List("user-1"));
    }

    [Fact]
    public void Add_WithValidFolder_PersistsAndReturnsRecord()
    {
        var store = new FileProjectStore(_appDataRoot);
        var folderPath = CreateRealFolder("my-project");
        var segments = folderPath.Split(IoPath.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);

        var added = store.Add("user-1", segments);

        Assert.Equal("my-project", added.Name);
        Assert.Single(store.List("user-1"));
    }

    [Fact]
    public void Add_WithInvalidFolder_ThrowsAndDoesNotCorruptExistingList()
    {
        var store = new FileProjectStore(_appDataRoot);
        var validFolder = CreateRealFolder("existing-project");
        store.Add("user-1", validFolder.Split(IoPath.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries));

        var missingSegments = new[] { _appDataRoot, "does-not-exist" };
        Assert.Throws<InvalidProjectPathException>(() => store.Add("user-1", missingSegments));

        Assert.Single(store.List("user-1"));
    }

    [Fact]
    public void Remove_RemovesOnlyTheListEntry_NotTheFolder()
    {
        var store = new FileProjectStore(_appDataRoot);
        var folderPath = CreateRealFolder("to-remove");
        var added = store.Add("user-1", folderPath.Split(IoPath.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries));

        store.Remove("user-1", added.Id);

        Assert.Empty(store.List("user-1"));
        Assert.True(Directory.Exists(folderPath));
    }

    [Fact]
    public void List_AfterReconstructingStore_StillReflectsPersistedProjects()
    {
        var store = new FileProjectStore(_appDataRoot);
        var folderPath = CreateRealFolder("survives-restart");
        store.Add("user-1", folderPath.Split(IoPath.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries));

        // Simulate a process restart: a brand-new store instance re-reading the same file.
        var reloadedStore = new FileProjectStore(_appDataRoot);

        Assert.Single(reloadedStore.List("user-1"));
    }
}
