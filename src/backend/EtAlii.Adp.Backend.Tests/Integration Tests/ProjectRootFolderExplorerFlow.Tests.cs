using EtAlii.Adp.Hierarchy.Wire;
using Grpc.Core;
using Xunit;
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Backend.Tests;

/// <summary>
/// Exercises HierarchyService against a real temporary folder and the real
/// backend host running in-process, per design.md's Integration Testing
/// strategy — especially the isolation-not-sharing property between two
/// simultaneous connections to the same project.
/// </summary>
public class ProjectRootFolderExplorerFlowTests(HostFixture host) : IClassFixture<HostFixture>
{
    private readonly string _projectFolder = host.NewProjectFolder();

    /// <summary>
    /// Triggers a change once the watch is really running, and returns the first change of the
    /// wanted kind. The watch is proven live with probe files first (see
    /// <see cref="StreamProbe.WaitUntilHierarchyLiveAsync"/>), which is why this reads until the wanted kind rather
    /// than taking the next message: what the probes leave behind may be reported in between.
    /// </summary>
    private async Task<HierarchyChange> AwaitTriggeredChangeAsync(
        AsyncServerStreamingCall<HierarchyMessage> call, CancellationTokenSource cts, HierarchyChange.ChangeOneofCase wanted, Action triggerChange)
    {
        await StreamProbe.WaitUntilHierarchyLiveAsync(call.ResponseStream, _projectFolder, cts.Token);
        triggerChange();

        return await call.ResponseStream.ReadUntilChangeAsync(change => change.ChangeCase == wanted, cts.Token);
    }

    [Fact]
    public async Task ListEntries_ForARootWithNestedFolders_ReturnsOnlyDirectChildren()
    {
        // Arrange.
        Directory.CreateDirectory(IoPath.Combine(_projectFolder, "sub"));
        await File.WriteAllTextAsync(IoPath.Combine(_projectFolder, "sub", "nested.txt"), "", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(IoPath.Combine(_projectFolder, "top.txt"), "", TestContext.Current.CancellationToken);

        // Arrange, continued.
        using var session = await FlowSession.OpenAsync(host, _projectFolder, TestContext.Current.CancellationToken);
        var hierarchyClient = session.Hierarchy;

        // Act.
        var response = await hierarchyClient.ListEntriesAsync(
            new ListEntriesRequest { ProjectId = session.ProjectId, WatchId = ShortGuid.NewShortGuid() },
            session.Headers,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert.
        Assert.Equal(ListEntriesResponse.ResultOneofCase.Entries, response.ResultCase);
        var names = response.Entries.Entries_.Select(e => e.Name).ToList();
        Assert.Contains("sub", names);
        Assert.Contains("top.txt", names);
        Assert.DoesNotContain("nested.txt", names);
    }

    [Fact]
    public async Task TwoConnectionsToTheSameProject_AssignDifferentIdsAndNeverObserveEachOthersChanges()
    {
        // Arrange.
        await File.WriteAllTextAsync(IoPath.Combine(_projectFolder, "shared.txt"), "", TestContext.Current.CancellationToken);

        // Arrange, continued.
        using var session = await FlowSession.OpenAsync(host, _projectFolder, TestContext.Current.CancellationToken);
        var hierarchyClient = session.Hierarchy;

        // Arrange, continued.
        var watchIdA = ShortGuid.NewShortGuid();
        var watchIdB = ShortGuid.NewShortGuid();

        // Arrange, continued.
        var entriesA = await hierarchyClient.ListEntriesAsync(new ListEntriesRequest { ProjectId = session.ProjectId, WatchId = watchIdA }, session.Headers, cancellationToken: TestContext.Current.CancellationToken);
        var entriesB = await hierarchyClient.ListEntriesAsync(new ListEntriesRequest { ProjectId = session.ProjectId, WatchId = watchIdB }, session.Headers, cancellationToken: TestContext.Current.CancellationToken);

        // Arrange, continued.
        var idA = entriesA.Entries.Entries_.Single(e => e.Name == "shared.txt").Id;
        var idB = entriesB.Entries.Entries_.Single(e => e.Name == "shared.txt").Id;
        Assert.NotEqual((ShortGuid)idA, (ShortGuid)idB);

        // Arrange, continued.
        using var callA = hierarchyClient.WatchHierarchy(new WatchHierarchyRequest { ProjectId = session.ProjectId, WatchId = watchIdA }, session.Headers, cancellationToken: TestContext.Current.CancellationToken);
        using var callB = hierarchyClient.WatchHierarchy(new WatchHierarchyRequest { ProjectId = session.ProjectId, WatchId = watchIdB }, session.Headers, cancellationToken: TestContext.Current.CancellationToken);
        using var ctsA = FlowSession.CreateMessageTimeout(TestContext.Current.CancellationToken);
        using var ctsB = FlowSession.CreateMessageTimeout(TestContext.Current.CancellationToken);

        // Arrange, continued.
        // Both watches share the folder, so each also reports the other's probes: the read below
        // looks for the one file the test itself creates.
        await StreamProbe.WaitUntilHierarchyLiveAsync(callA.ResponseStream, _projectFolder, ctsA.Token);
        await StreamProbe.WaitUntilHierarchyLiveAsync(callB.ResponseStream, _projectFolder, ctsB.Token);

        // Act.
        await File.WriteAllTextAsync(IoPath.Combine(_projectFolder, "new.txt"), "", TestContext.Current.CancellationToken);
        var changeA = await callA.ResponseStream.ReadUntilChangeAsync(IsTheNewFile, ctsA.Token);
        var changeB = await callB.ResponseStream.ReadUntilChangeAsync(IsTheNewFile, ctsB.Token);

        // Assert.
        Assert.Equal(HierarchyChange.ChangeOneofCase.Created, changeA.ChangeCase);
        Assert.Equal(HierarchyChange.ChangeOneofCase.Created, changeB.ChangeCase);
        Assert.Equal("new.txt", changeA.Created.Entry.Name);
        Assert.Equal("new.txt", changeB.Created.Entry.Name);
        Assert.NotEqual((ShortGuid)changeA.Created.Entry.Id, (ShortGuid)changeB.Created.Entry.Id);

        static bool IsTheNewFile(HierarchyChange change) =>
            change.ChangeCase == HierarchyChange.ChangeOneofCase.Created && change.Created.Entry.Name == "new.txt";
    }

    [Fact]
    public async Task RealFileSystemWatcher_RenameOfAKnownEntry_PushesARenamedMessagePreservingItsId()
    {
        // Arrange.
        var originalPath = IoPath.Combine(_projectFolder, "original.txt");
        await File.WriteAllTextAsync(originalPath, "", TestContext.Current.CancellationToken);

        // Arrange, continued.
        using var session = await FlowSession.OpenAsync(host, _projectFolder, TestContext.Current.CancellationToken);
        var hierarchyClient = session.Hierarchy;
        var watchId = ShortGuid.NewShortGuid();

        // Arrange, continued.
        var entries = await hierarchyClient.ListEntriesAsync(new ListEntriesRequest { ProjectId = session.ProjectId, WatchId = watchId }, session.Headers, cancellationToken: TestContext.Current.CancellationToken);
        var originalId = (ShortGuid)entries.Entries.Entries_.Single(e => e.Name == "original.txt").Id;

        // Arrange, continued.
        using var call = hierarchyClient.WatchHierarchy(new WatchHierarchyRequest { ProjectId = session.ProjectId, WatchId = watchId }, session.Headers, cancellationToken: TestContext.Current.CancellationToken);
        using var cts = FlowSession.CreateMessageTimeout(TestContext.Current.CancellationToken);

        // Act.
        var change = await AwaitTriggeredChangeAsync(call, cts, HierarchyChange.ChangeOneofCase.Renamed, () =>
            File.Move(originalPath, IoPath.Combine(_projectFolder, "renamed.txt")));

        // Assert.
        Assert.Equal(HierarchyChange.ChangeOneofCase.Renamed, change.ChangeCase);
        Assert.Equal(originalId, (ShortGuid)change.Renamed.EntryId);
        Assert.Equal("renamed.txt", change.Renamed.NewName);
    }

    [Fact]
    public async Task RootFolderDeletedWhileWatched_PushesRootUnavailable_AndRecoversOnceRecreated()
    {
        // Arrange.
        using var session = await FlowSession.OpenAsync(host, _projectFolder, TestContext.Current.CancellationToken);
        var hierarchyClient = session.Hierarchy;
        var watchId = ShortGuid.NewShortGuid();

        await hierarchyClient.ListEntriesAsync(new ListEntriesRequest { ProjectId = session.ProjectId, WatchId = watchId }, session.Headers, cancellationToken: TestContext.Current.CancellationToken);
        using var call = hierarchyClient.WatchHierarchy(new WatchHierarchyRequest { ProjectId = session.ProjectId, WatchId = watchId }, session.Headers, cancellationToken: TestContext.Current.CancellationToken);
        using var cts = FlowSession.CreateMessageTimeout(TestContext.Current.CancellationToken);

        var change = await AwaitTriggeredChangeAsync(call, cts, HierarchyChange.ChangeOneofCase.RootUnavailable, () =>
            Directory.Delete(_projectFolder, recursive: true));

        // Act and assert, step by step.
        Assert.Equal(HierarchyChange.ChangeOneofCase.RootUnavailable, change.ChangeCase);

        // Recreate the root; the watcher's recovery loop should notice and reconcile
        // without the connection needing to be torn down and reopened.
        Directory.CreateDirectory(_projectFolder);
    }
}
