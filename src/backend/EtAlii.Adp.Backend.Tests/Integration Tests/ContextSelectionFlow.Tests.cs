using EtAlii.Adp.Context.Wire;
using EtAlii.Adp.Documents.Wire;
using EtAlii.Adp.Hierarchy;
using EtAlii.Adp.Hierarchy.Wire;
using Grpc.Core;
using Xunit;
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here
using Path = EtAlii.Adp.Documents.Wire.Path;

namespace EtAlii.Adp.Backend.Tests;

/// <summary>
/// Exercises selection end to end against the real host: the baseline, a pushed
/// selection with its filled-in path, detail and actions, previews, on-disk renames and
/// deletes flowing back as selection changes, and - the property most worth having -
/// one connection never observing another's selection.
/// </summary>
public class ContextSelectionFlowTests(HostFixture host) : IClassFixture<HostFixture>
{
    private readonly string _projectFolder = host.NewProjectFolder();

    private Task<FlowSession> OpenSessionAsync() =>
        FlowSession.OpenAsync(host, _projectFolder, TestContext.Current.CancellationToken);

    private static Task<Documents.Wire.ShortGuid> EntryIdOfAsync(FlowSession session, string name) =>
        // A registration is a child of its subject now, so the lookup follows the nesting.
        NestedEntryLookup.EntryIdOfAsync(session.Hierarchy, session.ProjectId, session.WatchId, session.Headers, name);

    private static ContextSelection Selection(Documents.Wire.ShortGuid entryId, params string[] path)
    {
        var selection = new ContextSelection
        {
            Source = ContextSelectionSource.Explorer,
            Id = new ContextSource { EntryId = entryId },
            Path = new Path(),
        };
        selection.Path.Segments.AddRange(path);
        return selection;
    }

    private static async Task<ContextSelectionChanged> ReadSelectionAsync(IAsyncStreamReader<ContextMessage> stream, CancellationToken cancellationToken)
    {
        while (await stream.MoveNext(cancellationToken))
        {
            if (stream.Current.MessageCase == ContextMessage.MessageOneofCase.Selection)
            {
                return stream.Current.Selection;
            }
        }

        throw new InvalidOperationException("The stream ended before a selection message arrived.");
    }

    /// <summary>
    /// The full opening of a stream: the selection baseline, the project's own actions - undo
    /// and redo, on their own message (diagram-undo-redo Deviation 1) - and the project's
    /// problems (errors-and-warnings-panel Requirement 1.2). A test that then asserts nothing
    /// further arrives reads past all three here.
    /// </summary>
    private static async Task TestBaselineAsync(IAsyncStreamReader<ContextMessage> stream, CancellationToken cancellationToken)
    {
        _ = await ReadSelectionAsync(stream, cancellationToken);
        while (await stream.MoveNext(cancellationToken))
        {
            // Past the NEVER_VALIDATED baseline to the answer of the validation that opening the
            // project starts, so nothing that validation pushes arrives during the test itself.
            if (stream.Current.MessageCase == ContextMessage.MessageOneofCase.Problems && stream.Current.Problems.State == ProblemSetState.Validated)
            {
                return;
            }
        }

        throw new InvalidOperationException("The stream ended before the project's first validation arrived.");
    }

    [Fact]
    public async Task Watch_OpensWithAnEmptyBaseline()
    {
        // Arrange.
        using var session = await OpenSessionAsync();
        using var cts = FlowSession.CreateMessageTimeout(TestContext.Current.CancellationToken);

        // Act.
        using var call = session.Context.Watch(new WatchContextRequest { ProjectId = session.ProjectId, WatchId = session.WatchId }, session.Headers, cancellationToken: TestContext.Current.CancellationToken);
        var baseline = await ReadSelectionAsync(call.ResponseStream, cts.Token);

        // Assert.
        Assert.Null(baseline.Selection);
        Assert.Empty(baseline.Levels);
        Assert.False(baseline.Transient);
    }

    [Fact]
    public async Task Select_AFile_PushesTheChainWithFilledPathDetailAndActions()
    {
        // Arrange.
        Directory.CreateDirectory(IoPath.Combine(_projectFolder, "docs"));
        await File.WriteAllTextAsync(IoPath.Combine(_projectFolder, "docs", "design.mm"), "", TestContext.Current.CancellationToken);
        using var session = await OpenSessionAsync();
        using var cts = FlowSession.CreateMessageTimeout(TestContext.Current.CancellationToken);
        var docsId = await EntryIdOfAsync(session, "docs");
        var listed = await session.Hierarchy.ListEntriesAsync(
            new ListEntriesRequest { ProjectId = session.ProjectId, WatchId = session.WatchId, FolderId = docsId }, session.Headers, cancellationToken: TestContext.Current.CancellationToken);
        var fileId = listed.Entries.Entries_.Single().Id;

        using var call = session.Context.Watch(new WatchContextRequest { ProjectId = session.ProjectId, WatchId = session.WatchId }, session.Headers, cancellationToken: TestContext.Current.CancellationToken);
        await ReadSelectionAsync(call.ResponseStream, cts.Token);

        // Act and assert, step by step.
        // An empty path asks the backend to fill it in.
        var response = await session.Context.SelectAsync(
            new SelectRequest { ProjectId = session.ProjectId, WatchId = session.WatchId, Selection = Selection(fileId) }, session.Headers, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("", response.Error);

        var pushed = await ReadSelectionAsync(call.ResponseStream, cts.Token);
        Assert.Equal(new[] { "docs", "design.mm" }, pushed.Selection.Path.Segments);
        Assert.Equal(fileId, pushed.Selection.Id.EntryId);
        Assert.Equal(ContextSelectionSource.Explorer, pushed.Selection.Source);
        Assert.Equal(ContextSelection.DetailOneofCase.None_, pushed.Selection.DetailCase);
        var detail = Assert.Single(pushed.Levels);
        Assert.Equal(EntryKind.File, detail.Entry.Kind);
        Assert.True(detail.Entry.Available);
        // A bare .mm body routes by its declared extension: the pushed detail says which
        // canvas can open it (diagram-workspace-tabs Requirement 1.1).
        Assert.Equal("freeplane/mindmap", detail.Entry.DiagramMimeType);
        var actionIds = pushed.Actions.ActionIds();
        Assert.Contains(HierarchyContextActionProvider.RenameActionId, actionIds);
        Assert.Contains(HierarchyContextActionProvider.DeleteActionId, actionIds);
    }

    [Fact]
    public async Task Select_PushesTheDiagramTypeForARegistration_AndTheEditorTypeForAPlainFile()
    {
        // Arrange.
        // The whole tab system hangs off this one field arriving on the pushed detail
        // (diagram-workspace-tabs Requirement 1), so it is proven over the real resolver,
        // router and stream rather than against mocks.
        await File.WriteAllTextAsync(IoPath.Combine(_projectFolder, "domain.adp"), "freeplane/mindmap\n", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(IoPath.Combine(_projectFolder, "domain.mm"), "<map version=\"freeplane 1.11.5\">\n<node TEXT=\"domain\" ID=\"ID_1\"/>\n</map>\n", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(IoPath.Combine(_projectFolder, "readme.txt"), "", TestContext.Current.CancellationToken);
        using var session = await OpenSessionAsync();
        using var cts = FlowSession.CreateMessageTimeout(TestContext.Current.CancellationToken);
        var adpId = await EntryIdOfAsync(session, "domain.adp");
        var plainId = await EntryIdOfAsync(session, "readme.txt");

        using var call = session.Context.Watch(new WatchContextRequest { ProjectId = session.ProjectId, WatchId = session.WatchId }, session.Headers, cancellationToken: TestContext.Current.CancellationToken);
        await ReadSelectionAsync(call.ResponseStream, cts.Token);

        // Act and assert, step by step.
        await session.Context.SelectAsync(
            new SelectRequest { ProjectId = session.ProjectId, WatchId = session.WatchId, Selection = Selection(adpId) }, session.Headers, cancellationToken: TestContext.Current.CancellationToken);
        var diagram = await ReadSelectionAsync(call.ResponseStream, cts.Token);
        Assert.Equal("freeplane/mindmap", Assert.Single(diagram.Levels).Entry.DiagramMimeType);

        await session.Context.SelectAsync(
            new SelectRequest { ProjectId = session.ProjectId, WatchId = session.WatchId, Selection = Selection(plainId) }, session.Headers, cancellationToken: TestContext.Current.CancellationToken);
        var plain = await ReadSelectionAsync(call.ResponseStream, cts.Token);
        // The plain-text editor module is deployed in this host, so a file no diagram claims
        // carries its editor's mime rather than none (modular-text-editors task 5.4).
        Assert.Equal("editor/plain", Assert.Single(plain.Levels).Entry.DiagramMimeType);
    }

    [Fact]
    public async Task Select_WithAMismatchingPath_IsRejectedAndPushesNothing()
    {
        // Arrange.
        await File.WriteAllTextAsync(IoPath.Combine(_projectFolder, "a.txt"), "", TestContext.Current.CancellationToken);
        using var session = await OpenSessionAsync();
        using var cts = FlowSession.CreateMessageTimeout(TestContext.Current.CancellationToken);
        var entryId = await EntryIdOfAsync(session, "a.txt");
        using var call = session.Context.Watch(new WatchContextRequest { ProjectId = session.ProjectId, WatchId = session.WatchId }, session.Headers, cancellationToken: TestContext.Current.CancellationToken);
        await TestBaselineAsync(call.ResponseStream, cts.Token);

        // Act.
        var response = await session.Context.SelectAsync(
            new SelectRequest { ProjectId = session.ProjectId, WatchId = session.WatchId, Selection = Selection(entryId, "b.txt") }, session.Headers, cancellationToken: TestContext.Current.CancellationToken);

        // Assert.
        Assert.NotEqual("", response.Error);
        var pending = call.ResponseStream.MoveNext(cts.Token);
        Assert.False(
            await Eventually.TrueAsync(() => pending.IsCompleted, TestContext.Current.CancellationToken, TimeSpan.FromMilliseconds(500)),
            "A rejected Select must not push anything.");
    }

    [Fact]
    public async Task Select_Preview_PushesTransientAndLeavesTheCurrentSelectionAlone()
    {
        // Arrange.
        await File.WriteAllTextAsync(IoPath.Combine(_projectFolder, "current.txt"), "", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(IoPath.Combine(_projectFolder, "preview.txt"), "", TestContext.Current.CancellationToken);
        using var session = await OpenSessionAsync();
        using var cts = FlowSession.CreateMessageTimeout(TestContext.Current.CancellationToken);
        var currentId = await EntryIdOfAsync(session, "current.txt");
        var previewId = await EntryIdOfAsync(session, "preview.txt");
        using var call = session.Context.Watch(new WatchContextRequest { ProjectId = session.ProjectId, WatchId = session.WatchId }, session.Headers, cancellationToken: TestContext.Current.CancellationToken);
        await ReadSelectionAsync(call.ResponseStream, cts.Token);
        await session.Context.SelectAsync(
            new SelectRequest { ProjectId = session.ProjectId, WatchId = session.WatchId, Selection = Selection(currentId) }, session.Headers, cancellationToken: TestContext.Current.CancellationToken);
        await ReadSelectionAsync(call.ResponseStream, cts.Token);

        // Arrange, continued.
        var preview = Selection(previewId);
        preview.Action = ContextSelectionAction.Preview;
        await session.Context.SelectAsync(
            new SelectRequest { ProjectId = session.ProjectId, WatchId = session.WatchId, Selection = preview }, session.Headers, cancellationToken: TestContext.Current.CancellationToken);
        var transient = await ReadSelectionAsync(call.ResponseStream, cts.Token);
        Assert.True(transient.Transient);
        Assert.Equal(new[] { "preview.txt" }, transient.Selection.Path.Segments);

        // Act.
        // An action without a source runs against what is actually current - which the
        // preview must not have touched.
        await session.Context.ExecuteActionAsync(
            new ExecuteActionRequest
            {
                ProjectId = session.ProjectId,
                WatchId = session.WatchId,
                InteractionId = ShortGuid.NewShortGuid(),
                ActionId = HierarchyContextActionProvider.RenameActionId,
            },
            session.Headers,
            cancellationToken: TestContext.Current.CancellationToken);
        while (await call.ResponseStream.MoveNext(cts.Token))
        {
            if (call.ResponseStream.Current.MessageCase == ContextMessage.MessageOneofCase.Prompt)
            {
                Assert.Equal("current.txt", call.ResponseStream.Current.Prompt.InputDialog.InitialValue);
                return;
            }
        }

        // Assert.
        Assert.Fail("No prompt arrived for the action on the current selection.");
    }

    [Fact]
    public async Task RenameOnDisk_PushesTheSelectionWithItsNewPathAndTheSameId()
    {
        // Arrange.
        var original = IoPath.Combine(_projectFolder, "original.txt");
        await File.WriteAllTextAsync(original, "", TestContext.Current.CancellationToken);
        using var session = await OpenSessionAsync();
        using var cts = FlowSession.CreateMessageTimeout(TestContext.Current.CancellationToken);
        var entryId = await EntryIdOfAsync(session, "original.txt");

        using var hierarchyCall = session.Hierarchy.WatchHierarchy(new WatchHierarchyRequest { ProjectId = session.ProjectId, WatchId = session.WatchId }, session.Headers, cancellationToken: TestContext.Current.CancellationToken);
        await StreamProbe.WaitUntilHierarchyLiveAsync(hierarchyCall.ResponseStream, _projectFolder, cts.Token);
        _ = hierarchyCall.ResponseStream.MoveNext(cts.Token);
        using var call = session.Context.Watch(new WatchContextRequest { ProjectId = session.ProjectId, WatchId = session.WatchId }, session.Headers, cancellationToken: TestContext.Current.CancellationToken);
        await ReadSelectionAsync(call.ResponseStream, cts.Token);
        await session.Context.SelectAsync(
            new SelectRequest { ProjectId = session.ProjectId, WatchId = session.WatchId, Selection = Selection(entryId) }, session.Headers, cancellationToken: TestContext.Current.CancellationToken);
        await ReadSelectionAsync(call.ResponseStream, cts.Token);

        File.Move(original, IoPath.Combine(_projectFolder, "renamed.txt"));

        // Act and assert, step by step.
        var pushed = await ReadSelectionAsync(call.ResponseStream, cts.Token);
        Assert.Equal(new[] { "renamed.txt" }, pushed.Selection.Path.Segments);
        Assert.Equal(entryId, pushed.Selection.Id.EntryId);
    }

    [Fact]
    public async Task DeleteOnDisk_PushesAnEmptySelection()
    {
        // Arrange.
        var file = IoPath.Combine(_projectFolder, "doomed.txt");
        await File.WriteAllTextAsync(file, "", TestContext.Current.CancellationToken);
        using var session = await OpenSessionAsync();
        using var cts = FlowSession.CreateMessageTimeout(TestContext.Current.CancellationToken);
        var entryId = await EntryIdOfAsync(session, "doomed.txt");

        using var hierarchyCall = session.Hierarchy.WatchHierarchy(new WatchHierarchyRequest { ProjectId = session.ProjectId, WatchId = session.WatchId }, session.Headers, cancellationToken: TestContext.Current.CancellationToken);
        await StreamProbe.WaitUntilHierarchyLiveAsync(hierarchyCall.ResponseStream, _projectFolder, cts.Token);
        _ = hierarchyCall.ResponseStream.MoveNext(cts.Token);
        using var call = session.Context.Watch(new WatchContextRequest { ProjectId = session.ProjectId, WatchId = session.WatchId }, session.Headers, cancellationToken: TestContext.Current.CancellationToken);
        await ReadSelectionAsync(call.ResponseStream, cts.Token);
        await session.Context.SelectAsync(
            new SelectRequest { ProjectId = session.ProjectId, WatchId = session.WatchId, Selection = Selection(entryId) }, session.Headers, cancellationToken: TestContext.Current.CancellationToken);
        await ReadSelectionAsync(call.ResponseStream, cts.Token);

        File.Delete(file);

        // Act and assert, step by step.
        var pushed = await ReadSelectionAsync(call.ResponseStream, cts.Token);
        Assert.Null(pushed.Selection);
    }

    [Fact]
    public async Task Select_WithAnIdFromAnotherConnection_IsRejected()
    {
        // Arrange.
        await File.WriteAllTextAsync(IoPath.Combine(_projectFolder, "shared.txt"), "", TestContext.Current.CancellationToken);
        using var session = await OpenSessionAsync();
        var foreignWatchId = ShortGuid.NewShortGuid();
        var foreignEntries = await session.Hierarchy.ListEntriesAsync(
            new ListEntriesRequest { ProjectId = session.ProjectId, WatchId = foreignWatchId }, session.Headers, cancellationToken: TestContext.Current.CancellationToken);
        var foreignId = foreignEntries.Entries.Entries_.Single().Id;

        // Act.
        var response = await session.Context.SelectAsync(
            new SelectRequest { ProjectId = session.ProjectId, WatchId = session.WatchId, Selection = Selection(foreignId) }, session.Headers, cancellationToken: TestContext.Current.CancellationToken);

        // Assert.
        Assert.NotEqual("", response.Error);
    }

    [Fact]
    public async Task ASelectionOnOneConnection_IsNeverObservedOnAnothersStream()
    {
        // Arrange.
        await File.WriteAllTextAsync(IoPath.Combine(_projectFolder, "shared.txt"), "", TestContext.Current.CancellationToken);
        using var session = await OpenSessionAsync();
        using var ctsA = FlowSession.CreateMessageTimeout(TestContext.Current.CancellationToken);
        using var ctsB = FlowSession.CreateMessageTimeout(TestContext.Current.CancellationToken);
        var watchIdB = ShortGuid.NewShortGuid();
        var entryIdA = await EntryIdOfAsync(session, "shared.txt");

        using var callA = session.Context.Watch(new WatchContextRequest { ProjectId = session.ProjectId, WatchId = session.WatchId }, session.Headers, cancellationToken: TestContext.Current.CancellationToken);
        using var callB = session.Context.Watch(new WatchContextRequest { ProjectId = session.ProjectId, WatchId = watchIdB }, session.Headers, cancellationToken: TestContext.Current.CancellationToken);
        await TestBaselineAsync(callA.ResponseStream, ctsA.Token);
        await TestBaselineAsync(callB.ResponseStream, ctsB.Token);
        var pendingB = callB.ResponseStream.MoveNext(ctsB.Token);

        await session.Context.SelectAsync(
            new SelectRequest { ProjectId = session.ProjectId, WatchId = session.WatchId, Selection = Selection(entryIdA) }, session.Headers, cancellationToken: TestContext.Current.CancellationToken);

        // Act and assert, step by step.
        var pushedA = await ReadSelectionAsync(callA.ResponseStream, ctsA.Token);
        Assert.Equal(new[] { "shared.txt" }, pushedA.Selection.Path.Segments);
        Assert.False(
            await Eventually.TrueAsync(() => pendingB.IsCompleted, TestContext.Current.CancellationToken, TimeSpan.FromSeconds(1)),
            "Connection B observed a selection made on connection A.");
    }

    [Fact]
    public async Task SelectAndWatch_ForAProjectTheCallerIsNotAuthorizedFor_AreRejected()
    {
        // Arrange.
        using var owner = await OpenSessionAsync();
        using var otherChannel = host.CreateChannel();
        var otherHeaders = new Metadata { { FlowConstants.SessionTokenHeader, "not-a-valid-session" } };
        var otherClient = new ContextService.ContextServiceClient(otherChannel);

        // Act and assert, step by step.
        var selectFailure = await Assert.ThrowsAsync<RpcException>(async () => await otherClient.SelectAsync(
            new SelectRequest { ProjectId = owner.ProjectId, WatchId = owner.WatchId }, otherHeaders, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(StatusCode.Unauthenticated, selectFailure.StatusCode);

        using var cts = FlowSession.CreateMessageTimeout(TestContext.Current.CancellationToken);
        using var call = otherClient.Watch(new WatchContextRequest { ProjectId = owner.ProjectId, WatchId = owner.WatchId }, otherHeaders, cancellationToken: TestContext.Current.CancellationToken);
        var watchFailure = await Assert.ThrowsAsync<RpcException>(async () => await call.ResponseStream.MoveNext(cts.Token));
        Assert.Equal(StatusCode.Unauthenticated, watchFailure.StatusCode);
    }
}
