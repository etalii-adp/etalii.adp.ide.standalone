using Xunit;
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Diagram.WardleyMap.Tests;

/// <summary>
/// The store's read side against the world's write side: a load must succeed while a writer
/// holds the file, per the sharing discipline SharedDocumentReader carries. Windows enforces
/// sharing, so this guard bites there; a load refused here used to become an empty map pushed
/// over a full one.
/// </summary>
public sealed class WardleyDocumentStoreSharedReadTests : IDisposable
{
    private readonly string _folder = IoPath.Combine(IoPath.GetTempPath(), $"wardley-shared-{Guid.NewGuid():N}");

    public WardleyDocumentStoreSharedReadTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        TestFolder.TryDelete(_folder);
    }

    [Fact]
    public void GetOrLoad_ReadsADocumentAnEditorIsStillWriting()
    {
        // Arrange: the handle every save holds - write access, sharing only reads, the mode
        // File.WriteAllText opens with.
        const string text = "title Shared\ncomponent A [0.5, 0.5]\n";
        var path = IoPath.Combine(_folder, "map.owm");
        File.WriteAllText(path, text);
        using var editor = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.Read);

        // Act.
        var document = new WardleyDocumentStore().GetOrLoad(path);

        // Assert: the map itself, not the empty fallback.
        Assert.Equal(text, document.ToText());
    }
}
