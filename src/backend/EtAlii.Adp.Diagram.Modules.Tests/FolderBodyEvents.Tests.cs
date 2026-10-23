using EtAlii.Adp.Files;
using EtAlii.Adp.Specification.Fbl.Plugins;
using Xunit;

namespace EtAlii.Adp.Diagram.Modules.Tests;

/// <summary>
/// What a watcher heard under a folder, translated once for every module that keeps a folder body open
/// (code-structure T294): each file event becomes the paths the folder body is told, in order.
/// </summary>
public class FolderBodyEventsTests
{
    private const string Folder = "/projects/site/chart";

    private readonly List<string> _notified = [];

    [Theory]
    [InlineData(FileEventKind.Created)]
    [InlineData(FileEventKind.Changed)]
    [InlineData(FileEventKind.Deleted)]
    public void AFileEvent_NotifiesItsPath(FileEventKind kind)
    {
        // Act.
        FolderBodyEvents.Notify(_notified.Add, Folder, new FileEvent(kind, Folder + "/values.yaml"));

        // Assert.
        Assert.Equal([Folder + "/values.yaml"], _notified);
    }

    [Fact]
    public void ARename_NotifiesTheOldNameAndThenTheNew()
    {
        // Act: a file renamed away is as much a change as one renamed in.
        FolderBodyEvents.Notify(_notified.Add, Folder, new FileEvent(FileEventKind.Renamed, Folder + "/new.yaml", Folder + "/old.yaml"));

        // Assert.
        Assert.Equal([Folder + "/old.yaml", Folder + "/new.yaml"], _notified);
    }

    [Fact]
    public void ARenameWithoutItsOldName_NotifiesTheNewOne()
    {
        // Act.
        FolderBodyEvents.Notify(_notified.Add, Folder, new FileEvent(FileEventKind.Renamed, Folder + "/new.yaml"));

        // Assert.
        Assert.Equal([Folder + "/new.yaml"], _notified);
    }

    [Fact]
    public void ALostEvent_NotifiesTheFolderItself_NotThePathItCameWith()
    {
        // Act: heard through the project root's watcher, a Lost names that root; which file changed is
        // unknowable, so the folder is read again.
        FolderBodyEvents.Notify(_notified.Add, Folder, new FileEvent(FileEventKind.Lost, "/projects/site"));

        // Assert.
        Assert.Equal([Folder], _notified);
    }

    [Fact]
    public void APathTheFolderBodyRefuses_IsSwallowed_AndTheNextIsStillHandedOver()
    {
        // Arrange: 'Notify' runs on the watcher's thread, where an exception ends the process.

        // Act.
        var exception = Record.Exception(() =>
            FolderBodyEvents.Notify(Notify, Folder, new FileEvent(FileEventKind.Renamed, Folder + "/new.yaml", Folder + "/old.yaml")));

        // Assert.
        Assert.Null(exception);
        Assert.Equal([Folder + "/new.yaml"], _notified);
        return;

        void Notify(string path)
        {
            if (path.EndsWith("old.yaml", StringComparison.Ordinal)) throw new ArgumentException("refused", nameof(path));
            _notified.Add(path);
        }
    }

    [Fact]
    public void NoFolderBody_IsRefused()
    {
        // Act and assert.
        Assert.Throws<ArgumentNullException>(() => FolderBodyEvents.Notify((FolderBody)null!, Folder, new FileEvent(FileEventKind.Changed, Folder)));
    }
}
