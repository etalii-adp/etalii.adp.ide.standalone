using EtAlii.Adp.Context;
using EtAlii.Adp.Documents.Wire;
using EtAlii.Adp.Files;
using Microsoft.Extensions.Time.Testing;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Hierarchy.Tests;

/// <summary>
/// The hierarchy model fed through the <see cref="SplitMoveJoiner"/>, as <see cref="ProjectFileEvents"/>
/// feeds it on Linux: a file moved on disk keeps its entry, its id and a selection on it.
/// </summary>
public class SplitMoveTests : IDisposable
{
    private static readonly TimeSpan _grace = TimeSpan.FromSeconds(1);

    private readonly string _root;
    private readonly FakeTimeProvider _time = new();

    public SplitMoveTests()
    {
        _root = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        TestFolder.TryDelete(_root);
        GC.SuppressFinalize(this);
    }

    private string CreateFile(string name)
    {
        var path = IoPath.Combine(_root, name);
        File.WriteAllText(path, "");
        return path;
    }

    [Fact]
    public async Task ASplitMove_KeepsTheTrackedSelectionOnTheEntry_AndItsId()
    {
        // Arrange.
        // The CI failure of ContextSelectionFlowTests.RenameOnDisk at unit scale: a file
        // selected, then renamed on disk, with the watcher reporting the move split in two. Fed
        // to the model as it came, the Deleted removed the entry and the selection tracking it
        // reported it gone - an empty selection pushed where the renamed one belonged.
        using var store = new HierarchyModelStore(TestToolRouters.For(), TimeSpan.FromMinutes(5));
        var resolver = new HierarchyContextSourceResolver(store, new DiagramFileRouter(new EmptyCatalog()), new EditorResolver(new Editor.EditorDefinitionCatalog { All = [] }));
        var watchId = ShortGuid.NewShortGuid();
        var model = store.GetOrCreate(watchId, ProjectRoot.From(_root));
        var original = CreateFile("original.txt");
        var entryId = model.ListChildren(null).Single(n => n.Name == "original.txt").Id;
        var resolution = await resolver.ResolveAsync(
            watchId, _root, new ContextSource { EntryId = entryId }, [], parent: null, TestContext.Current.CancellationToken);
        var level = Assert.IsType<ResolvedContextLevel>(resolution).Level;
        var reported = new List<IReadOnlyList<string>?>();
        using var track = resolver.Track(watchId, _root, level, reported.Add);
        using var joiner = new SplitMoveJoiner(model.OnFileEvent, _grace, _time);
        var renamed = IoPath.Combine(_root, "renamed.txt");
        File.Move(original, renamed);

        // Act.
        joiner.Post(new FileEvent(FileEventKind.Deleted, original));
        joiner.Post(new FileEvent(FileEventKind.Created, renamed));
        _time.Advance(_grace);

        // Assert.
        Assert.Equal(["renamed.txt"], Assert.Single(reported));
        Assert.True(model.TryResolvePath(entryId, out var fullPath, out _));
        Assert.Equal(renamed, fullPath);
    }
}
