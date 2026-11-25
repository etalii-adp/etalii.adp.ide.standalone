using EtAlii.Adp.Files;
using Xunit;

namespace EtAlii.Adp.Links.Tests;

/// <summary>
/// The workspace's index of links over a real folder and its real watcher (etalii.adp spec 092, T014): what a tracked root
/// holds when it is first read, and how a file created, changed, renamed, deleted or edited while open changes it. The
/// watcher's events arrive in their own time, so a test waits for what they cause rather than for a guessed delay.
/// </summary>
public sealed class LinkIndexTests : IDisposable
{
    private readonly TestFolder _folder = TestFolder.Create("link-index");
    private readonly ProjectFileEvents _events = new();
    private readonly LinkIndexTestIndexer _indexer = new();
    private readonly LinkIndex _index;

    public LinkIndexTests() => _index = new LinkIndex(_events, [_indexer]);

    public void Dispose()
    {
        _index.Dispose();
        _events.Dispose();
        _folder.Dispose();
    }

    [Fact]
    public void ATrackedRoot_HoldsEveryDocumentItsToolTypesClaim_AndResolvesTheirLinks()
    {
        // Arrange.
        Write("release.lnk.txt", "id: r1", "element: m1 Launch");
        Write(Path.Combine("notes", "ideas.lnk.txt"), "id: i1", "element: n1 Idea", "link: n1 $r1@release.lnk.txt#m1=Launch");
        Write("readme.md", "Not a document any indexer claims.");

        // Act.
        using var tracked = _index.Track(_folder.Path);
        var workspace = _index.WorkspaceOf(Held("notes", "ideas.lnk.txt"));

        // Assert.
        Assert.NotNull(workspace);
        Assert.Equal([Held("notes", "ideas.lnk.txt"), Held("release.lnk.txt")], workspace.Documents.Select(document => document.Path));
        var resolution = workspace.Resolve("$r1@release.lnk.txt#m1=Launch");
        Assert.True(resolution.Resolved);
        Assert.Equal("m1", resolution.Element?.Id);
        Assert.Single(workspace.Backlinks(Held("release.lnk.txt"), "m1"));
    }

    [Fact]
    public async Task AFileCreatedAfterwards_IsIndexed_AndAnnounced()
    {
        // Arrange.
        using var tracked = _index.Track(_folder.Path);
        var announced = new List<string>();
        _index.Changed += (_, changed) => { lock (announced) announced.Add(changed.Path); };

        // Act.
        Write("release.lnk.txt", "id: r1", "element: m1 Launch");

        // Assert.
        Assert.True(await Eventually.TrueAsync(() => _index.WorkspaceOf(Held("release.lnk.txt"))?.Document(Held("release.lnk.txt")) is not null, TestContext.Current.CancellationToken));
        Assert.True(await Eventually.TrueAsync(() => { lock (announced) return announced.Contains(Held("release.lnk.txt")); }, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AFileChanged_IsReadAgain_AndALinkToItsNewNameResolves()
    {
        // Arrange.
        Write("release.lnk.txt", "id: r1", "element: m1 Launch");
        using var tracked = _index.Track(_folder.Path);

        // Act.
        Write("release.lnk.txt", "id: r1", "element: m1 Go live");

        // Assert: the link names the target's new name once it is read again (FBL 17.4).
        Assert.True(await Eventually.TrueAsync(() => _index.WorkspaceOf(Held("release.lnk.txt"))?.Resolve("$r1@release.lnk.txt#m1=Launch").Refreshed?.Name == "Go live", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AFileRenamed_IsHeldAtItsNewPath_AndStillResolvesByItsId()
    {
        // Arrange.
        Write("release.lnk.txt", "id: r1", "element: m1 Launch");
        using var tracked = _index.Track(_folder.Path);

        // Act.
        File.Move(_folder.Combine("release.lnk.txt"), _folder.Combine("launch.lnk.txt"));

        // Assert: found by its id under the name it has now, the name the link is written again with (FBL 17.3, 17.4).
        Assert.True(await Eventually.TrueAsync(() => _index.WorkspaceOf(Held("launch.lnk.txt")) is { } workspace && workspace.Document(Held("release.lnk.txt")) is null && workspace.Resolve("$r1@release.lnk.txt#m1=Launch").Refreshed?.Text == "$r1@launch.lnk.txt#m1=Launch", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AFileDeleted_IsTakenOut_AndALinkToItBreaks()
    {
        // Arrange.
        Write("release.lnk.txt", "id: r1", "element: m1 Launch");
        using var tracked = _index.Track(_folder.Path);

        // Act.
        File.Delete(_folder.Combine("release.lnk.txt"));

        // Assert.
        Assert.True(await Eventually.TrueAsync(() => _index.WorkspaceOf(Held("release.lnk.txt"))?.Resolve("$r1@release.lnk.txt#m1=Launch").Finding?.Code == "link.broken-document", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AFolderDeleted_TakesOutEveryDocumentInIt()
    {
        // Arrange.
        Write(Path.Combine("notes", "a.lnk.txt"), "id: a1");
        Write(Path.Combine("notes", "b.lnk.txt"), "id: b1");
        Write("c.lnk.txt", "id: c1");
        using var tracked = _index.Track(_folder.Path);

        // Act.
        Directory.Delete(_folder.Combine("notes"), recursive: true);

        // Assert.
        Assert.True(await Eventually.TrueAsync(() => _index.WorkspaceOf(Held("c.lnk.txt"))?.Documents.Select(document => document.Path).SequenceEqual([Held("c.lnk.txt")]) == true, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void AFolderRenamed_HoldsEveryDocumentInItAtItsNewPath()
    {
        // Arrange: the watcher stopped, so the one event the folder's move makes is the one the test raises.
        Write(Path.Combine("notes", "a.lnk.txt"), "id: a1");
        Write(Path.Combine("notes", "b.lnk.txt"), "id: b1");
        Write("c.lnk.txt", "id: c1");
        using var tracked = _index.Track(_folder.Path);
        var watcher = _events.Watcher(_folder.Path)!;
        watcher.EnableRaisingEvents = false;
        Directory.Move(_folder.Combine("notes"), _folder.Combine("archive"));

        // Act.
        watcher.RaiseRenamed(_folder.Combine("notes"), _folder.Combine("archive"));

        // Assert.
        Assert.Equal([Held("archive", "a.lnk.txt"), Held("archive", "b.lnk.txt"), Held("c.lnk.txt")], _index.WorkspaceOf(Held("c.lnk.txt"))!.Documents.Select(document => document.Path));
    }

    [Fact]
    public async Task AnOpenDocument_IsHeldAsItIsOpen_UntilItIsReverted()
    {
        // Arrange.
        Write("release.lnk.txt", "id: r1", "element: m1 Launch");
        using var tracked = _index.Track(_folder.Path);
        var path = Held("release.lnk.txt");

        // Act: an edit not yet saved, then a write of the file by something else.
        _index.Edited(path, "id: r1\nelement: m1 Unsaved"u8.ToArray());
        Write("release.lnk.txt", "id: r1", "element: m1 On disk");
        Write("marker.lnk.txt", "id: z9");
        Assert.True(await Eventually.TrueAsync(() => _index.WorkspaceOf(path)?.Document(Held("marker.lnk.txt")) is not null, TestContext.Current.CancellationToken));

        // Assert: what is open wins over the file until it is closed without a save.
        Assert.Equal("Unsaved", NameOf(path, "m1"));
        _index.Reverted(path);
        Assert.Equal("On disk", NameOf(path, "m1"));
    }

    [Fact]
    public void AWorkspaceHandedOut_IsNotChangedByALaterChange()
    {
        // Arrange.
        Write("release.lnk.txt", "id: r1", "element: m1 Launch");
        using var tracked = _index.Track(_folder.Path);
        var path = Held("release.lnk.txt");
        var before = _index.WorkspaceOf(path)!;

        // Act.
        _index.Edited(path, "id: r1\nelement: m1 Changed"u8.ToArray());

        // Assert: a resolution running on another thread reads one state whole.
        Assert.Equal("Launch", before.Document(path)?.Elements[0].ValueOf("name"));
        Assert.Equal("Changed", NameOf(path, "m1"));
    }

    [Fact]
    public void AFileOverTheSizeLimit_IsNotRead()
    {
        // Arrange.
        Write("small.lnk.txt", "id: s1");
        File.WriteAllBytes(_folder.Combine("large.lnk.txt"), new byte[LinkIndex.SizeLimit + 1]);

        // Act.
        using var tracked = _index.Track(_folder.Path);

        // Assert.
        Assert.Equal([Held("small.lnk.txt")], _index.WorkspaceOf(Held("small.lnk.txt"))!.Documents.Select(document => document.Path));
        Assert.Equal(1, _indexer.Reads);
    }

    [Fact]
    public void ADocumentThatCannotBeRead_HoldsNothing_AndCostsTheOthersNothing()
    {
        // Arrange.
        Write("broken.lnk.txt", "throw");
        Write("fine.lnk.txt", "id: f1");

        // Act.
        using var tracked = _index.Track(_folder.Path);

        // Assert.
        Assert.Equal([Held("fine.lnk.txt")], _index.WorkspaceOf(Held("fine.lnk.txt"))!.Documents.Select(document => document.Path));
    }

    [Fact]
    public void ARootTrackedTwice_IsReadOnce_AndFollowedUntilBothLetGo()
    {
        // Arrange.
        Write("release.lnk.txt", "id: r1");
        var first = _index.Track(_folder.Path);
        var second = _index.Track(_folder.Path);

        // Act and assert.
        Assert.Equal(1, _indexer.Reads);
        first.Dispose();
        Assert.NotNull(_index.WorkspaceOf(Held("release.lnk.txt")));
        second.Dispose();
        Assert.Null(_index.WorkspaceOf(Held("release.lnk.txt")));
        Assert.Equal(0, _events.WatchedRoots());
    }

    [Fact]
    public async Task LostEvents_ReadTheRootAgainWhole()
    {
        // Arrange: a file the watcher, stopped, does not report.
        using var tracked = _index.Track(_folder.Path);
        var watcher = _events.Watcher(_folder.Path)!;
        watcher.EnableRaisingEvents = false;
        Write("release.lnk.txt", "id: r1");

        // Act.
        watcher.RaiseOverflow();

        // Assert.
        Assert.True(await Eventually.TrueAsync(() => _index.WorkspaceOf(Held("release.lnk.txt"))?.Document(Held("release.lnk.txt")) is not null, TestContext.Current.CancellationToken));
    }

    private void Write(string relative, params string[] lines)
    {
        var path = _folder.Combine(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, string.Join('\n', lines));
    }

    /// <summary>A path under the folder as the index holds it: full, with forward slashes.</summary>
    private string Held(params string[] relative) => Path.GetFullPath(_folder.Combine(relative)).Replace('\\', '/');

    private string? NameOf(string path, string elementId) =>
        _index.WorkspaceOf(path)?.Document(path)?.Elements.Single(element => element.Id == elementId).ValueOf("name");
}
