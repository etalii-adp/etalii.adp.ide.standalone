using EtAlii.Adp.Backend.Hierarchy;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Backend.Tests;

public class HierarchyModelTests : IDisposable
{
    private readonly string _root;

    public HierarchyModelTests()
    {
        _root = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private string CreateFolder(params string[] segments)
    {
        var path = IoPath.Combine(new[] { _root }.Concat(segments).ToArray());
        Directory.CreateDirectory(path);
        return path;
    }

    private string CreateFile(string content = "", params string[] segments)
    {
        var path = IoPath.Combine(new[] { _root }.Concat(segments).ToArray());
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void ListChildren_OnRoot_ReturnsFoldersBeforeFilesThenAlphabetical()
    {
        CreateFile(segments: "b.txt");
        CreateFile(segments: "a.txt");
        CreateFolder("zeta");
        CreateFolder("alpha");
        var model = new HierarchyModel(_root);

        var children = model.ListChildren(null);

        Assert.Equal(new[] { "alpha", "zeta", "a.txt", "b.txt" }, children.Select(c => c.Name));
        Assert.True(children[0].IsFolder);
        Assert.True(children[1].IsFolder);
        Assert.False(children[2].IsFolder);
        Assert.False(children[3].IsFolder);
    }

    [Fact]
    public void ListChildren_CalledTwiceForTheSameFolder_ReturnsStableIds()
    {
        CreateFile(segments: "a.txt");
        var model = new HierarchyModel(_root);

        var first = model.ListChildren(null).Single();
        var second = model.ListChildren(null).Single();

        Assert.Equal(first.Id, second.Id);
    }

    [Fact]
    public void OnWatcherEvent_Created_UnderAListedParent_RaisesCreatedWithParentIdNameAndKind()
    {
        var model = new HierarchyModel(_root);
        model.ListChildren(null); // root now "listed"

        HierarchyEntryChange? raised = null;
        model.EntryChanged += change => raised = change;

        var newFolder = CreateFolder("new-folder");
        model.OnWatcherEvent(WatcherChangeTypes.Created, null, newFolder);

        var created = Assert.IsType<HierarchyEntryChange.Created>(raised);
        Assert.Null(created.Entry.ParentId);
        Assert.Equal("new-folder", created.Entry.Name);
        Assert.True(created.Entry.IsFolder);
    }

    [Fact]
    public void OnWatcherEvent_Created_UnderAnUnlistedFolder_IsDiscarded()
    {
        var unlisted = CreateFolder("unlisted");
        var model = new HierarchyModel(_root);
        var unlistedEntry = model.ListChildren(null).Single(); // root listed, but "unlisted"'s own children are not

        HierarchyEntryChange? raised = null;
        model.EntryChanged += change => raised = change;

        var newFile = CreateFile(segments: ["unlisted", "inside.txt"]);
        model.OnWatcherEvent(WatcherChangeTypes.Created, null, newFile);

        // The new file itself is discarded (never listed here), but "unlisted" flips from
        // empty to non-empty regardless - that's what unlocks its expand affordance.
        var updated = Assert.IsType<HierarchyEntryChange.Updated>(raised);
        Assert.NotEmpty(unlisted);
        Assert.Equal(unlistedEntry.Id, updated.EntryId);
        Assert.True(updated.HasChildren);
    }

    [Fact]
    public void AddEntry_ForANewlyCreatedFolder_ReflectsWhetherItAlreadyHasChildrenOnDisk()
    {
        var populated = CreateFolder("populated");
        CreateFile(segments: ["populated", "inside.txt"]);
        CreateFolder("empty");
        var model = new HierarchyModel(_root);

        var children = model.ListChildren(null);

        Assert.NotEmpty(populated);
        Assert.True(children.Single(c => c.Name == "populated").HasChildren);
        Assert.False(children.Single(c => c.Name == "empty").HasChildren);
    }

    [Fact]
    public void ListChildren_ReSyncingAKnownFolder_RefreshesHasChildren()
    {
        var folderPath = CreateFolder("sub");
        var model = new HierarchyModel(_root);
        var before = model.ListChildren(null).Single();
        Assert.False(before.HasChildren);

        CreateFile(segments: ["sub", "new.txt"]);
        var after = model.ListChildren(null).Single();

        Assert.NotEmpty(folderPath);
        Assert.True(after.HasChildren);
    }

    [Fact]
    public void OnWatcherEvent_Removed_LastChildOfAFolder_PushesUpdatedWithHasChildrenFalse()
    {
        var folderPath = CreateFolder("sub");
        var filePath = CreateFile(segments: ["sub", "only.txt"]);
        var model = new HierarchyModel(_root);
        var folderEntry = model.ListChildren(null).Single();
        Assert.True(folderEntry.HasChildren);

        HierarchyEntryChange? raised = null;
        model.EntryChanged += change => raised = change;

        File.Delete(filePath);
        model.OnWatcherEvent(WatcherChangeTypes.Deleted, filePath, null);

        var updated = Assert.IsType<HierarchyEntryChange.Updated>(raised);
        Assert.NotEmpty(folderPath);
        Assert.Equal(folderEntry.Id, updated.EntryId);
        Assert.False(updated.HasChildren);
    }

    [Fact]
    public void RecomputeHasChildren_WhenValueDoesNotChange_RaisesNoEvent()
    {
        CreateFolder("sub");
        CreateFile(segments: ["sub", "a.txt"]);
        var model = new HierarchyModel(_root);
        model.ListChildren(null); // "sub" already known to have children

        var raised = false;
        model.EntryChanged += _ => raised = true;

        // A second file lands in "sub" - it already had children, so HasChildren doesn't change.
        var secondFile = CreateFile(segments: ["sub", "b.txt"]);
        model.OnWatcherEvent(WatcherChangeTypes.Created, null, secondFile);

        Assert.False(raised);
    }

    [Fact]
    public void OnWatcherEvent_Deleted_ForAKnownEntry_RaisesRemovedWithExistingId()
    {
        var filePath = CreateFile(segments: "a.txt");
        var model = new HierarchyModel(_root);
        var entry = model.ListChildren(null).Single();

        HierarchyEntryChange? raised = null;
        model.EntryChanged += change => raised = change;

        File.Delete(filePath);
        model.OnWatcherEvent(WatcherChangeTypes.Deleted, filePath, null);

        var removed = Assert.IsType<HierarchyEntryChange.Removed>(raised);
        Assert.Equal(entry.Id, removed.EntryId);
    }

    [Fact]
    public void OnWatcherEvent_Renamed_ForAKnownEntry_RaisesRenamedWithExistingIdAndNewName()
    {
        var filePath = CreateFile(segments: "old.txt");
        var model = new HierarchyModel(_root);
        var entry = model.ListChildren(null).Single();

        HierarchyEntryChange? raised = null;
        model.EntryChanged += change => raised = change;

        var newPath = IoPath.Combine(_root, "new.txt");
        model.OnWatcherEvent(WatcherChangeTypes.Renamed, filePath, newPath);

        var renamed = Assert.IsType<HierarchyEntryChange.Renamed>(raised);
        Assert.Equal(entry.Id, renamed.EntryId);
        Assert.Equal("new.txt", renamed.NewName);
    }

    [Fact]
    public void OnWatcherEvent_RenamedFolder_PreservesIdsOfDescendants()
    {
        CreateFolder("old-name");
        var childFile = CreateFile(segments: ["old-name", "child.txt"]);
        var model = new HierarchyModel(_root);
        var folderEntry = model.ListChildren(null).Single();
        var childEntry = model.ListChildren(folderEntry.Id).Single();

        var oldFolderPath = IoPath.Combine(_root, "old-name");
        var newFolderPath = IoPath.Combine(_root, "new-name");
        Directory.Move(oldFolderPath, newFolderPath);
        model.OnWatcherEvent(WatcherChangeTypes.Renamed, oldFolderPath, newFolderPath);

        var childrenAfterRename = model.ListChildren(folderEntry.Id);
        var childAfterRename = Assert.Single(childrenAfterRename);

        Assert.NotEmpty(childFile);
        Assert.Equal(childEntry.Id, childAfterRename.Id);
    }

    [Fact]
    public void Reconcile_PreservesIdsForSurvivingEntries_AndAssignsNewIdsForNewOnes()
    {
        var survivorPath = CreateFile(segments: "survivor.txt");
        var victimPath = CreateFile(segments: "victim.txt");
        var model = new HierarchyModel(_root);
        var before = model.ListChildren(null);
        var survivorId = before.Single(e => e.Name == "survivor.txt").Id;

        // Simulate a buffer overflow: disk changes happen without OnWatcherEvent ever being called.
        File.Delete(victimPath);
        CreateFile(segments: "newcomer.txt");

        model.Reconcile();

        var after = model.ListChildren(null);
        Assert.NotEmpty(survivorPath);
        Assert.Equal(new[] { "newcomer.txt", "survivor.txt" }, after.Select(e => e.Name).OrderBy(n => n));
        Assert.Equal(survivorId, after.Single(e => e.Name == "survivor.txt").Id);
    }

    [Fact]
    public void ListChildren_ExcludesASymlinkThatEscapesTheRoot()
    {
        CreateFolder("real");
        var outsideRoot = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outsideRoot);
        try
        {
            var symlinkPath = IoPath.Combine(_root, "escape");
            try
            {
                Directory.CreateSymbolicLink(symlinkPath, outsideRoot);
            }
            catch (Exception)
            {
                // Creating a directory symlink requires a privilege this environment may not
                // grant (e.g. no Developer Mode / not elevated) - nothing to assert without it.
                return;
            }

            var model = new HierarchyModel(_root);
            var children = model.ListChildren(null);

            Assert.Contains(children, c => c.Name == "real");
            Assert.DoesNotContain(children, c => c.Name == "escape");
        }
        finally
        {
            Directory.Delete(outsideRoot, recursive: true);
        }
    }

    [Fact]
    public void TryResolvePath_ForAKnownEntry_YieldsItsCurrentLocationAndKind()
    {
        var filePath = CreateFile(segments: "a.txt");
        CreateFolder("sub");
        var model = new HierarchyModel(_root);
        var children = model.ListChildren(null);

        Assert.True(model.TryResolvePath(children.Single(c => c.Name == "a.txt").Id, out var resolvedFile, out var fileIsFolder));
        Assert.True(model.TryResolvePath(children.Single(c => c.Name == "sub").Id, out _, out var subIsFolder));

        Assert.Equal(IoPath.GetFullPath(filePath), resolvedFile);
        Assert.False(fileIsFolder);
        Assert.True(subIsFolder);
    }

    [Fact]
    public void TryResolvePath_ForAnIdThisModelDoesNotKnow_ResolvesToNothing()
    {
        var model = new HierarchyModel(_root);

        var resolved = model.TryResolvePath(ShortGuid.NewShortGuid(), out var path, out _);

        Assert.False(resolved);
        Assert.Equal("", path);
    }

    [Fact]
    public void TryResolvePath_ForAnEntryTurnedIntoASymlinkEscapingTheRoot_ResolvesToNothing()
    {
        var outsideRoot = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outsideRoot);
        try
        {
            var entryPath = CreateFolder("swapped");
            var model = new HierarchyModel(_root);
            var entryId = model.ListChildren(null).Single(c => c.Name == "swapped").Id;

            // Swap the real folder for a symlink pointing outside, exactly the case
            // resolving straight from the listing-time path would walk into.
            Directory.Delete(entryPath);
            try
            {
                Directory.CreateSymbolicLink(entryPath, outsideRoot);
            }
            catch (Exception)
            {
                // Creating a directory symlink requires a privilege this environment may not
                // grant (e.g. no Developer Mode / not elevated) - nothing to assert without it.
                return;
            }

            Assert.False(model.TryResolvePath(entryId, out _, out _));
        }
        finally
        {
            Directory.Delete(outsideRoot, recursive: true);
        }
    }
    [Fact]
    public void OnWatcherEvent_ForTheWritersScratchFile_RaisesNothingAndListsNothing()
    {
        // AdpFileWriter writes a new diagram to a scratch file in the destination folder and
        // moves it into place; neither step is project content, so neither may reach a client.
        var model = new HierarchyModel(_root);
        model.ListChildren(null);
        var changes = new List<HierarchyEntryChange>();
        model.EntryChanged += changes.Add;

        var scratch = IoPath.Combine(_root, $"{AdpFileWriter.TempPrefix}0123456789abcdef{AdpFileWriter.TempExtension}");
        File.WriteAllText(scratch, "freeplane/mindmap\n");
        model.OnWatcherEvent(WatcherChangeTypes.Created, null, scratch);

        Assert.Empty(changes);
        Assert.Empty(model.ListChildren(null));
    }

    [Fact]
    public void OnWatcherEvent_ForTheMoveThatPublishesADiagram_ReportsOnlyTheCreatedFile()
    {
        var model = new HierarchyModel(_root);
        model.ListChildren(null);
        var changes = new List<HierarchyEntryChange>();
        model.EntryChanged += changes.Add;

        var scratch = IoPath.Combine(_root, $"{AdpFileWriter.TempPrefix}0123456789abcdef{AdpFileWriter.TempExtension}");
        var published = IoPath.Combine(_root, "domain.adp");
        File.WriteAllText(scratch, "freeplane/mindmap\n");
        model.OnWatcherEvent(WatcherChangeTypes.Created, null, scratch);
        Assert.Empty(changes);

        File.Move(scratch, published);
        model.OnWatcherEvent(WatcherChangeTypes.Renamed, scratch, published);

        // The move is the only announcement the published file gets - a watcher reports it as
        // a rename, not a create - so it must arrive as the creation it actually is.
        var created = Assert.IsType<HierarchyEntryChange.Created>(Assert.Single(changes));
        Assert.Equal("domain.adp", created.Entry.Name);
        Assert.Equal("domain.adp", Assert.Single(model.ListChildren(null)).Name);
    }

    [Fact]
    public void Reconcile_NeverResurrectsAScratchFileLeftBehind()
    {
        var model = new HierarchyModel(_root);
        model.ListChildren(null);
        File.WriteAllText(IoPath.Combine(_root, $"{AdpFileWriter.TempPrefix}abandoned{AdpFileWriter.TempExtension}"), "");

        model.Reconcile();

        Assert.Empty(model.ListChildren(null));
    }
}