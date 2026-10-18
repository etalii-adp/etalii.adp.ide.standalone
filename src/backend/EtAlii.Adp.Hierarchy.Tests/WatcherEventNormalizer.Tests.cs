using EtAlii.Adp.Files;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Hierarchy.Tests;

/// <summary>
/// What <see cref="WatcherEventNormalizer"/> passes on to the hierarchy, on its own: the watcher's
/// shapes translated, write transients dropped, a published scratch file read as a create, and a
/// rename's echo dropped in each shape the watcher reports it.
/// </summary>
public class WatcherEventNormalizerTests
{
    private static readonly string Folder = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.Tests", "normalizer");
    private static readonly string Old = IoPath.Combine(Folder, "old.txt");
    private static readonly string New = IoPath.Combine(Folder, "new.txt");
    private static readonly string Scratch = IoPath.Combine(Folder, $"~adp-{Guid.NewGuid():N}.tmp");

    [Fact]
    public void Translate_EachWatcherShape_BecomesTheFileEventItReports()
    {
        // Act and assert.
        Assert.Equal(new FileEvent(FileEventKind.Created, New), WatcherEventNormalizer.Translate(WatcherChangeTypes.Created, null, New));
        Assert.Equal(new FileEvent(FileEventKind.Deleted, Old), WatcherEventNormalizer.Translate(WatcherChangeTypes.Deleted, Old, null));
        Assert.Equal(new FileEvent(FileEventKind.Renamed, New, Old), WatcherEventNormalizer.Translate(WatcherChangeTypes.Renamed, Old, New));
        Assert.Equal(new FileEvent(FileEventKind.Changed, New), WatcherEventNormalizer.Translate(WatcherChangeTypes.Changed, null, New));
        Assert.Null(WatcherEventNormalizer.Translate(WatcherChangeTypes.Renamed, Old, null));
    }

    [Fact]
    public void Normalize_AWriteTransient_IsDropped_AndItsPublishingMove_IsACreate()
    {
        // Arrange.
        var normalizer = new WatcherEventNormalizer();

        // Act and assert: the scratch file itself is never content...
        Assert.Null(normalizer.Normalize(new FileEvent(FileEventKind.Created, Scratch)));
        Assert.Null(normalizer.Normalize(new FileEvent(FileEventKind.Deleted, Scratch)));
        Assert.Null(normalizer.Normalize(new FileEvent(FileEventKind.Renamed, Scratch, New)));

        // ...but the move that publishes it is the only announcement the real file gets.
        Assert.Equal(new FileEvent(FileEventKind.Created, New), normalizer.Normalize(new FileEvent(FileEventKind.Renamed, New, Scratch)));
    }

    [Fact]
    public void Normalize_TheEchoOfAnExpectedRename_IsDropped_InEveryShape()
    {
        // Arrange.
        var normalizer = new WatcherEventNormalizer();
        normalizer.Expect(Old, New);

        // Act and assert: one Renamed on Windows, a Delete and a Create on Linux.
        Assert.Null(normalizer.Normalize(new FileEvent(FileEventKind.Renamed, New, Old)));
        Assert.Null(normalizer.Normalize(new FileEvent(FileEventKind.Deleted, Old)));
        Assert.Null(normalizer.Normalize(new FileEvent(FileEventKind.Created, New)));

        // An unrelated event still passes.
        var other = new FileEvent(FileEventKind.Created, IoPath.Combine(Folder, "other.txt"));
        Assert.Equal(other, normalizer.Normalize(other));
    }

    [Fact]
    public void Normalize_AWithdrawnRename_NoLongerHidesAGenuineDelete()
    {
        // Arrange.
        var normalizer = new WatcherEventNormalizer();
        normalizer.Expect(Old, New);
        normalizer.Withdraw(Old, New);
        var deleted = new FileEvent(FileEventKind.Deleted, Old);

        // Act.
        var normalized = normalizer.Normalize(deleted);

        // Assert.
        Assert.Equal(deleted, normalized);
    }
}
