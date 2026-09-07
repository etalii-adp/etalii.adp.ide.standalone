using EtAlii.Adp.Common;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Hierarchy.Tests;

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
        TestFolder.TryDelete(_root);
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
        // Arrange.
        CreateFile(segments: "b.txt");
        CreateFile(segments: "a.txt");
        CreateFolder("zeta");
        CreateFolder("alpha");
        var model = new HierarchyModel(_root);

        // Act.
        var children = model.ListChildren(null);

        // Assert.
        Assert.Equal(new[] { "alpha", "zeta", "a.txt", "b.txt" }, children.Select(c => c.Name));
        Assert.True(children[0].IsFolder);
        Assert.True(children[1].IsFolder);
        Assert.False(children[2].IsFolder);
        Assert.False(children[3].IsFolder);
    }

    [Fact]
    public void ListChildren_CalledTwiceForTheSameFolder_ReturnsStableIds()
    {
        // Arrange.
        CreateFile(segments: "a.txt");
        var model = new HierarchyModel(_root);

        // Act.
        var first = model.ListChildren(null).Single();
        var second = model.ListChildren(null).Single();

        // Assert.
        Assert.Equal(first.Id, second.Id);
    }

    [Fact]
    public void ApplyLocalRename_RaisesRenamedWithAStableId_AndSuppressesTheWindowsWatcherEcho()
    {
        // Arrange: a listed file, so the model knows its id.
        var oldPath = CreateFile(segments: "original.txt");
        var newPath = IoPath.Combine(_root, "renamed.txt");
        var model = new HierarchyModel(_root);
        var originalId = model.ListChildren(null).Single().Id;
        var changes = new List<HierarchyEntryChange>();
        model.EntryChanged += changes.Add;

        // Act: the command applies the rename directly, then the Windows watcher echoes the
        // same move as one Renamed event.
        File.Move(oldPath, newPath);
        model.ApplyLocalRename(oldPath, newPath);
        model.OnWatcherEvent(WatcherChangeTypes.Renamed, oldPath, newPath);

        // Assert: exactly one Renamed, the id preserved, the echo dropped.
        var renamed = Assert.IsType<HierarchyEntryRenamed>(Assert.Single(changes));
        Assert.Equal(originalId, renamed.EntryId);
        Assert.Equal("renamed.txt", renamed.NewName);
    }

    [Fact]
    public void ApplyLocalRename_SuppressesTheLinuxWatcherEcho_DeliveredAsDeleteThenCreate()
    {
        // Arrange: the same, but the watcher splits the move into a Delete and a Create - what
        // inotify does on Linux, where it does not correlate the two halves of a rename. Without
        // suppression this would land as Remove+Create and discard the entry's id.
        var oldPath = CreateFile(segments: "original.txt");
        var newPath = IoPath.Combine(_root, "renamed.txt");
        var model = new HierarchyModel(_root);
        var originalId = model.ListChildren(null).Single().Id;
        var changes = new List<HierarchyEntryChange>();
        model.EntryChanged += changes.Add;

        // Act.
        File.Move(oldPath, newPath);
        model.ApplyLocalRename(oldPath, newPath);
        model.OnWatcherEvent(WatcherChangeTypes.Deleted, oldPath, null);
        model.OnWatcherEvent(WatcherChangeTypes.Created, null, newPath);

        // Assert: still one Renamed with the same id; no Removed, no second Created.
        var renamed = Assert.IsType<HierarchyEntryRenamed>(Assert.Single(changes));
        Assert.Equal(originalId, renamed.EntryId);
        Assert.Equal("renamed.txt", renamed.NewName);
    }

    [Fact]
    public void OnWatcherEvent_Created_UnderAListedParent_RaisesCreatedWithParentIdNameAndKind()
    {
        // Arrange.
        var model = new HierarchyModel(_root);
        model.ListChildren(null); // root now "listed"

        HierarchyEntryChange? raised = null;
        model.EntryChanged += change => raised = change;

        var newFolder = CreateFolder("new-folder");
        model.OnWatcherEvent(WatcherChangeTypes.Created, null, newFolder);

        // Act and assert, step by step.
        var created = Assert.IsType<HierarchyEntryCreated>(raised);
        Assert.Null(created.Entry.ParentId);
        Assert.Equal("new-folder", created.Entry.Name);
        Assert.True(created.Entry.IsFolder);
    }

    [Fact]
    public void OnWatcherEvent_Created_UnderAnUnlistedFolder_IsDiscarded()
    {
        // Arrange.
        var unlisted = CreateFolder("unlisted");
        var model = new HierarchyModel(_root);
        var unlistedEntry = model.ListChildren(null).Single(); // root listed, but "unlisted"'s own children are not

        HierarchyEntryChange? raised = null;
        model.EntryChanged += change => raised = change;

        var newFile = CreateFile(segments: ["unlisted", "inside.txt"]);
        model.OnWatcherEvent(WatcherChangeTypes.Created, null, newFile);

        // Act and assert, step by step.
        // The new file itself is discarded (never listed here), but "unlisted" flips from
        // empty to non-empty regardless - that's what unlocks its expand affordance.
        var updated = Assert.IsType<HierarchyEntryUpdated>(raised);
        Assert.NotEmpty(unlisted);
        Assert.Equal(unlistedEntry.Id, updated.EntryId);
        Assert.True(updated.HasChildren);
    }

    [Fact]
    public void AddEntry_ForANewlyCreatedFolder_ReflectsWhetherItAlreadyHasChildrenOnDisk()
    {
        // Arrange.
        var populated = CreateFolder("populated");
        CreateFile(segments: ["populated", "inside.txt"]);
        CreateFolder("empty");
        var model = new HierarchyModel(_root);

        // Act.
        var children = model.ListChildren(null);

        // Assert.
        Assert.NotEmpty(populated);
        Assert.True(children.Single(c => c.Name == "populated").HasChildren);
        Assert.False(children.Single(c => c.Name == "empty").HasChildren);
    }

    [Fact]
    public void ListChildren_ReSyncingAKnownFolder_RefreshesHasChildren()
    {
        // Arrange.
        var folderPath = CreateFolder("sub");
        var model = new HierarchyModel(_root);
        var before = model.ListChildren(null).Single();
        Assert.False(before.HasChildren);

        // Act.
        CreateFile(segments: ["sub", "new.txt"]);
        var after = model.ListChildren(null).Single();

        // Assert.
        Assert.NotEmpty(folderPath);
        Assert.True(after.HasChildren);
    }

    [Fact]
    public void OnWatcherEvent_Removed_LastChildOfAFolder_PushesUpdatedWithHasChildrenFalse()
    {
        // Act and assert, step by step.
        var folderPath = CreateFolder("sub");
        var filePath = CreateFile(segments: ["sub", "only.txt"]);
        var model = new HierarchyModel(_root);
        var folderEntry = model.ListChildren(null).Single();
        Assert.True(folderEntry.HasChildren);

        HierarchyEntryChange? raised = null;
        model.EntryChanged += change => raised = change;

        File.Delete(filePath);
        model.OnWatcherEvent(WatcherChangeTypes.Deleted, filePath, null);

        var updated = Assert.IsType<HierarchyEntryUpdated>(raised);
        Assert.NotEmpty(folderPath);
        Assert.Equal(folderEntry.Id, updated.EntryId);
        Assert.False(updated.HasChildren);
    }

    [Fact]
    public void RecomputeHasChildren_WhenValueDoesNotChange_RaisesNoEvent()
    {
        // Arrange.
        CreateFolder("sub");
        CreateFile(segments: ["sub", "a.txt"]);
        var model = new HierarchyModel(_root);
        model.ListChildren(null); // "sub" already known to have children

        // Arrange, continued.
        var raised = false;
        model.EntryChanged += _ => raised = true;

        // Act.
        // A second file lands in "sub" - it already had children, so HasChildren doesn't change.
        var secondFile = CreateFile(segments: ["sub", "b.txt"]);
        model.OnWatcherEvent(WatcherChangeTypes.Created, null, secondFile);

        // Assert.
        Assert.False(raised);
    }

    [Fact]
    public void OnWatcherEvent_Deleted_ForAKnownEntry_RaisesRemovedWithExistingId()
    {
        // Arrange.
        var filePath = CreateFile(segments: "a.txt");
        var model = new HierarchyModel(_root);
        var entry = model.ListChildren(null).Single();

        HierarchyEntryChange? raised = null;
        model.EntryChanged += change => raised = change;

        File.Delete(filePath);
        model.OnWatcherEvent(WatcherChangeTypes.Deleted, filePath, null);

        // Act and assert, step by step.
        var removed = Assert.IsType<HierarchyEntryRemoved>(raised);
        Assert.Equal(entry.Id, removed.EntryId);
    }

    [Fact]
    public void OnWatcherEvent_Deleted_ForAPathThatStillExists_KeepsTheEntryAndItsId()
    {
        // A Deleted event whose path is still on disk is not a deletion: Windows' ReplaceFile -
        // which File.Replace uses, and AdpFileWriter.Save with it, to rewrite a registration in
        // place - raises a spurious Deleted for the destination it just refreshed. Removing the
        // entry here would hand the file a new id on the Created that follows, orphaning any open
        // document that still holds the old one. That is what left a causal-loop diagram
        // unselectable after the first variable drag: its positions live in the .adp's layout:
        // block, so every move rewrites the registration in place.
        var filePath = CreateFile(segments: "a.txt");
        var model = new HierarchyModel(_root);
        var entry = model.ListChildren(null).Single();

        var changes = new List<HierarchyEntryChange>();
        model.EntryChanged += changes.Add;

        // The file is deliberately left on disk - this is the replace-in-place echo.
        model.OnWatcherEvent(WatcherChangeTypes.Deleted, filePath, null);

        // The entry survives, with the very same id, so a document opened through it stays
        // resolvable; and nothing was pushed, because the tree did not change.
        Assert.DoesNotContain(changes, change => change is HierarchyEntryRemoved);
        Assert.True(model.TryResolvePath(entry.Id, out _, out _));
    }

    [Fact]
    public void OnWatcherEvent_ReplaceFileSequence_KeepsTheEntryAndItsId()
    {
        // The event sequence Windows' ReplaceFile actually raises when File.Replace rewrites a
        // file in place, verbatim from a live run: its backup temp appears, the DESTINATION is
        // renamed onto that backup name, the fresh content arrives under the real name as a
        // create, and the backup is deleted. Untreated, the rename walks the entry off its path
        // and the create then mints a NEW id for the same file - which orphaned every open
        // causal-loop diagram on the first variable drag, since its positions live in the
        // .adp's layout: block and every move rewrites that registration in place.
        var filePath = CreateFile(segments: "a.txt");
        var backupPath = filePath + "~RF17c7806.TMP";
        var model = new HierarchyModel(_root);
        var entry = model.ListChildren(null).Single();

        var changes = new List<HierarchyEntryChange>();
        model.EntryChanged += changes.Add;

        // The file itself stays on disk throughout - ReplaceFile never leaves the path empty.
        model.OnWatcherEvent(WatcherChangeTypes.Created, null, backupPath);
        model.OnWatcherEvent(WatcherChangeTypes.Renamed, filePath, backupPath);
        model.OnWatcherEvent(WatcherChangeTypes.Created, null, filePath);
        model.OnWatcherEvent(WatcherChangeTypes.Deleted, backupPath, null);

        // One entry, the same id, and no churn was pushed: to the tree, nothing happened.
        Assert.True(model.TryResolvePath(entry.Id, out var resolvedPath, out _));
        Assert.Equal(IoPath.GetFullPath(filePath), resolvedPath);
        Assert.Empty(changes);
        Assert.Equal(entry.Id, model.ListChildren(null).Single(e => e.Name == "a.txt").Id);
    }

    [Fact]
    public void OnWatcherEvent_Renamed_ForAKnownEntry_RaisesRenamedWithExistingIdAndNewName()
    {
        // Arrange.
        var filePath = CreateFile(segments: "old.txt");
        var model = new HierarchyModel(_root);
        var entry = model.ListChildren(null).Single();

        HierarchyEntryChange? raised = null;
        model.EntryChanged += change => raised = change;

        var newPath = IoPath.Combine(_root, "new.txt");
        model.OnWatcherEvent(WatcherChangeTypes.Renamed, filePath, newPath);

        // Act and assert, step by step.
        var renamed = Assert.IsType<HierarchyEntryRenamed>(raised);
        Assert.Equal(entry.Id, renamed.EntryId);
        Assert.Equal("new.txt", renamed.NewName);
    }

    [Fact]
    public void OnWatcherEvent_RenamedFolder_PreservesIdsOfDescendants()
    {
        // Arrange.
        CreateFolder("old-name");
        var childFile = CreateFile(segments: ["old-name", "child.txt"]);
        var model = new HierarchyModel(_root);
        var folderEntry = model.ListChildren(null).Single();
        var childEntry = model.ListChildren(folderEntry.Id).Single();

        // Arrange, continued.
        var oldFolderPath = IoPath.Combine(_root, "old-name");
        var newFolderPath = IoPath.Combine(_root, "new-name");
        Directory.Move(oldFolderPath, newFolderPath);
        model.OnWatcherEvent(WatcherChangeTypes.Renamed, oldFolderPath, newFolderPath);

        // Act.
        var childrenAfterRename = model.ListChildren(folderEntry.Id);
        var childAfterRename = Assert.Single(childrenAfterRename);

        // Assert.
        Assert.NotEmpty(childFile);
        Assert.Equal(childEntry.Id, childAfterRename.Id);
    }

    [Fact]
    public void Reconcile_PreservesIdsForSurvivingEntries_AndAssignsNewIdsForNewOnes()
    {
        // Arrange.
        var survivorPath = CreateFile(segments: "survivor.txt");
        var victimPath = CreateFile(segments: "victim.txt");
        var model = new HierarchyModel(_root);
        var before = model.ListChildren(null);
        var survivorId = before.Single(e => e.Name == "survivor.txt").Id;

        // Simulate a buffer overflow: disk changes happen without OnWatcherEvent ever being called.
        File.Delete(victimPath);
        CreateFile(segments: "newcomer.txt");

        model.Reconcile();

        // Act and assert, step by step.
        var after = model.ListChildren(null);
        Assert.NotEmpty(survivorPath);
        Assert.Equal(new[] { "newcomer.txt", "survivor.txt" }, after.Select(e => e.Name).OrderBy(n => n));
        Assert.Equal(survivorId, after.Single(e => e.Name == "survivor.txt").Id);
    }

    [Fact]
    public void ListChildren_ExcludesASymlinkThatEscapesTheRoot()
    {
        // Arrange.
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

        // Act.
            var model = new HierarchyModel(_root);
            var children = model.ListChildren(null);

        // Assert.
            Assert.Contains(children, c => c.Name == "real");
            Assert.DoesNotContain(children, c => c.Name == "escape");
        }
        finally
        {
            TestFolder.TryDelete(outsideRoot);
        }
    }

    [Fact]
    public void TryResolvePath_ForAKnownEntry_YieldsItsCurrentLocationAndKind()
    {
        // Arrange.
        var filePath = CreateFile(segments: "a.txt");
        CreateFolder("sub");
        var model = new HierarchyModel(_root);
        var children = model.ListChildren(null);

        // Act.
        Assert.True(model.TryResolvePath(children.Single(c => c.Name == "a.txt").Id, out var resolvedFile, out var fileIsFolder));
        Assert.True(model.TryResolvePath(children.Single(c => c.Name == "sub").Id, out _, out var subIsFolder));

        // Assert.
        Assert.Equal(IoPath.GetFullPath(filePath), resolvedFile);
        Assert.False(fileIsFolder);
        Assert.True(subIsFolder);
    }

    [Fact]
    public void TryResolvePath_ForAnIdThisModelDoesNotKnow_ResolvesToNothing()
    {
        // Arrange.
        var model = new HierarchyModel(_root);

        // Act.
        var resolved = model.TryResolvePath(ShortGuid.NewShortGuid(), out var path, out _);

        // Assert.
        Assert.False(resolved);
        Assert.Equal("", path);
    }

    [Fact]
    public void TryResolvePath_ForAnEntryTurnedIntoASymlinkEscapingTheRoot_ResolvesToNothing()
    {
        // Arrange.
        var outsideRoot = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outsideRoot);
        try
        {
            var entryPath = CreateFolder("swapped");
            var model = new HierarchyModel(_root);
            var entryId = model.ListChildren(null).Single(c => c.Name == "swapped").Id;

        // Act.
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

        // Assert.
            Assert.False(model.TryResolvePath(entryId, out _, out _));
        }
        finally
        {
            TestFolder.TryDelete(outsideRoot);
        }
    }
    [Fact]
    public void OnWatcherEvent_ForTheWritersScratchFile_RaisesNothingAndListsNothing()
    {
        // Arrange.
        // AdpFileWriter writes a new diagram to a scratch file in the destination folder and
        // moves it into place; neither step is project content, so neither may reach a client.
        var model = new HierarchyModel(_root);
        model.ListChildren(null);
        var changes = new List<HierarchyEntryChange>();
        model.EntryChanged += changes.Add;

        // Act.
        var scratch = IoPath.Combine(_root, $"{AdpFileWriter.TempPrefix}0123456789abcdef{AdpFileWriter.TempExtension}");
        File.WriteAllText(scratch, "freeplane/mindmap\n");
        model.OnWatcherEvent(WatcherChangeTypes.Created, null, scratch);

        // Assert.
        Assert.Empty(changes);
        Assert.Empty(model.ListChildren(null));
    }

    [Fact]
    public void OnWatcherEvent_ForTheMoveThatPublishesADiagram_ReportsOnlyTheCreatedFile()
    {
        // Arrange.
        var model = new HierarchyModel(_root);
        model.ListChildren(null);
        var changes = new List<HierarchyEntryChange>();
        model.EntryChanged += changes.Add;

        // Act and assert, step by step.
        var scratch = IoPath.Combine(_root, $"{AdpFileWriter.TempPrefix}0123456789abcdef{AdpFileWriter.TempExtension}");
        var published = IoPath.Combine(_root, "domain.adp");
        File.WriteAllText(scratch, "freeplane/mindmap\n");
        model.OnWatcherEvent(WatcherChangeTypes.Created, null, scratch);
        Assert.Empty(changes);

        File.Move(scratch, published);
        model.OnWatcherEvent(WatcherChangeTypes.Renamed, scratch, published);

        // The move is the only announcement the published file gets - a watcher reports it as
        // a rename, not a create - so it must arrive as the creation it actually is.
        var created = Assert.IsType<HierarchyEntryCreated>(Assert.Single(changes));
        Assert.Equal("domain.adp", created.Entry.Name);
        Assert.Equal("domain.adp", Assert.Single(model.ListChildren(null)).Name);
    }

    [Fact]
    public void Reconcile_NeverResurrectsAScratchFileLeftBehind()
    {
        // Arrange.
        var model = new HierarchyModel(_root);
        model.ListChildren(null);
        File.WriteAllText(IoPath.Combine(_root, $"{AdpFileWriter.TempPrefix}abandoned{AdpFileWriter.TempExtension}"), "");

        // Act.
        model.Reconcile();

        // Assert.
        Assert.Empty(model.ListChildren(null));
    }
}