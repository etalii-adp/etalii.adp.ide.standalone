using EtAlii.Adp.Files;
using Microsoft.Extensions.Time.Testing;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Documents.Tests;

/// <summary>
/// The Linux watcher reports a move as a Deleted and a Created when its two inotify halves
/// arrive more than 2 ms apart; the joiner delivers such a move as the Renamed it was, and
/// everything else as it came, in order.
/// </summary>
public class SplitMoveJoinerTests : IDisposable
{
    private readonly TestFolder _testFolder = TestFolder.Create("split-move-joiner");
    private readonly string _root;
    private static readonly TimeSpan _grace = TimeSpan.FromSeconds(1);

    private readonly List<FileEvent> _delivered = [];
    private readonly FakeTimeProvider _time = new();
    private readonly SplitMoveJoiner _joiner;

    public SplitMoveJoinerTests()
    {
        _root = _testFolder.Path;
        _joiner = new SplitMoveJoiner(_delivered.Add, _grace, _time);
    }

    public void Dispose()
    {
        _joiner.Dispose();
        _testFolder.Dispose();
    }

    private string CreateFile(string name)
    {
        var path = IoPath.Combine(_root, name);
        File.WriteAllText(path, "");
        return path;
    }

    [Fact]
    public void ADeleteThenACreate_OfAMovedFile_IsDeliveredAsOneRename()
    {
        // Arrange.
        var original = CreateFile("original.txt");
        var renamed = IoPath.Combine(_root, "renamed.txt");
        File.Move(original, renamed);

        // Act.
        _joiner.Post(new FileEvent(FileEventKind.Deleted, original));
        _joiner.Post(new FileEvent(FileEventKind.Created, renamed));

        // Assert.
        Assert.Equal([new FileEvent(FileEventKind.Renamed, renamed, original)], _delivered);
    }

    [Fact]
    public void ADelete_WithNothingAfterIt_IsDeliveredWhenTheGraceRunsOut()
    {
        // Arrange.
        var doomed = CreateFile("doomed.txt");
        File.Delete(doomed);

        // Act.
        _joiner.Post(new FileEvent(FileEventKind.Deleted, doomed));
        var heldBack = _delivered.ToList();
        _time.Advance(_grace);

        // Assert.
        Assert.Empty(heldBack);
        Assert.Equal([new FileEvent(FileEventKind.Deleted, doomed)], _delivered);
    }

    [Fact]
    public void ADelete_FollowedByAnotherKindOfEvent_IsDeliveredFirst()
    {
        // Arrange.
        var doomed = CreateFile("doomed.txt");
        var other = CreateFile("other.txt");
        File.Delete(doomed);

        // Act.
        _joiner.Post(new FileEvent(FileEventKind.Deleted, doomed));
        _joiner.Post(new FileEvent(FileEventKind.Changed, other));
        _time.Advance(_grace);

        // Assert.
        Assert.Equal([new FileEvent(FileEventKind.Deleted, doomed), new FileEvent(FileEventKind.Changed, other)], _delivered);
    }

    [Fact]
    public void ADeleteThenACreate_OfTheSamePath_IsNotARename()
    {
        // Arrange.
        // An editor that saves by deleting the file and writing it again: the old path is back.
        var saved = CreateFile("saved.txt");

        // Act.
        _joiner.Post(new FileEvent(FileEventKind.Deleted, saved));
        _joiner.Post(new FileEvent(FileEventKind.Created, saved));

        // Assert.
        Assert.Equal([new FileEvent(FileEventKind.Created, saved)], _delivered.Where(d => d.Kind != FileEventKind.Deleted).ToList());
        Assert.DoesNotContain(_delivered, d => d.Kind == FileEventKind.Renamed);
    }

    [Fact]
    public void ADeleteOfAPathThatStillExists_IsDeliveredAtOnce()
    {
        // Arrange.
        // Not half of a move - the model reads it as an in-place replace.
        var replaced = CreateFile("replaced.txt");

        // Act.
        _joiner.Post(new FileEvent(FileEventKind.Deleted, replaced));

        // Assert.
        Assert.Equal([new FileEvent(FileEventKind.Deleted, replaced)], _delivered);
    }

    [Fact]
    public void AHeldDelete_IsDroppedOnDispose()
    {
        // Arrange.
        var doomed = CreateFile("doomed.txt");
        File.Delete(doomed);
        _joiner.Post(new FileEvent(FileEventKind.Deleted, doomed));

        // Act.
        _joiner.Dispose();
        _time.Advance(_grace);

        // Assert.
        Assert.Empty(_delivered);
    }
}
