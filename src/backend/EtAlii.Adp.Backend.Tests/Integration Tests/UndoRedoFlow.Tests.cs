using EtAlii.Adp.Context;
using EtAlii.Adp.Context.Wire;
using EtAlii.Adp.Documents.Wire;
using EtAlii.Adp.Hierarchy;
using EtAlii.Adp.Hierarchy.Wire;
using EtAlii.Adp.TestSupport.Host;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Xunit;
using ContextService = EtAlii.Adp.Context.Wire.ContextService;
using HierarchyService = EtAlii.Adp.Hierarchy.Wire.HierarchyService;
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Backend.Tests;

/// <summary>
/// The whole undo/redo loop against the real host: a rename raised as a context action lands
/// on the project's history, its availability is pushed to every connection in that project,
/// executing <c>history.undo</c> reverses it on disk and the reversal flows back as an ordinary
/// hierarchy change. Plus the properties that only the real wiring proves: two projects keep
/// separate histories, two connections in one project share one, and a delete records nothing
/// (diagram-undo-redo Requirements 1, 2, 3.5).
/// </summary>
public class UndoRedoFlowTests(HostFixture host) : IClassFixture<HostFixture>
{
    private readonly string _projectFolder = host.NewProjectFolder();

    // ---- scenarios --------------------------------------------------------------------

    [Fact]
    public async Task RenameThroughAContextAction_MakesUndoAvailable_AndUndoReversesItOnDiskAndOnTheStream()
    {
        // Arrange.
        await File.WriteAllTextAsync(IoPath.Combine(_projectFolder, "original.txt"), "content", TestContext.Current.CancellationToken);
        using var session = await FlowSession.OpenAsync(host, _projectFolder, TestContext.Current.CancellationToken);
        var hierarchyClient = session.Hierarchy;
        var contextClient = session.Context;
        var watchId = ShortGuid.NewShortGuid();
        var entryId = await EntryIdOfAsync(hierarchyClient, session.ProjectId, watchId, session.Headers, "original.txt");

        using var hierarchyCall = hierarchyClient.WatchHierarchy(new WatchHierarchyRequest { ProjectId = session.ProjectId, WatchId = watchId }, session.Headers, cancellationToken: TestContext.Current.CancellationToken);
        using var cts = FlowSession.CreateMessageTimeout(TestContext.Current.CancellationToken);
        using var contextCall = contextClient.Watch(new WatchContextRequest { ProjectId = session.ProjectId, WatchId = watchId }, session.Headers, cancellationToken: TestContext.Current.CancellationToken);
        await StreamProbe.WaitUntilContextLiveAsync(contextCall.ResponseStream, cts.Token);
        await StreamProbe.WaitUntilHierarchyLiveAsync(hierarchyCall.ResponseStream, _projectFolder, cts.Token);

        // Act and assert, step by step.
        // Rename original.txt -> renamed.txt through the context action, then let the first
        // (forward) rename change drain off the hierarchy stream.
        var pendingForward = hierarchyCall.ResponseStream.ReadUntilChangeAsync(HierarchyChange.ChangeOneofCase.Renamed, cts.Token);
        await RenameViaContextAsync(contextClient, session.ProjectId, watchId, session.Headers, entryId, contextCall.ResponseStream, cts.Token, "renamed.txt");
        await pendingForward;
        Assert.True(File.Exists(IoPath.Combine(_projectFolder, "renamed.txt")));

        // The rename changed the history, so Undo is pushed as available to this connection.
        var afterRename = await ReadUntilProjectActionsAsync(contextCall.ResponseStream, cts.Token, undoAvailable: true);
        Assert.True(UndoOf(afterRename).Available);

        // Undo through the project-scoped action, with no prompt: it completes at once.
        var pendingReverse = hierarchyCall.ResponseStream.ReadUntilChangeAsync(HierarchyChange.ChangeOneofCase.Renamed, cts.Token);
        var undone = await ExecuteProjectActionAsync(contextClient, session.ProjectId, watchId, session.Headers, HistoryContextActionProvider.UndoActionId);
        Assert.True(undone.Accepted, undone.Error);

        var reverse = await pendingReverse;
        Assert.Equal("original.txt", reverse.Renamed.NewName);
        Assert.True(File.Exists(IoPath.Combine(_projectFolder, "original.txt")));
        Assert.False(File.Exists(IoPath.Combine(_projectFolder, "renamed.txt")));
    }

    [Fact]
    public async Task TwoProjects_KeepSeparateHistories_SoUndoInOneLeavesTheOtherUntouched()
    {
        // Arrange.
        var otherFolder = host.NewProjectFolder("other-project");
        await File.WriteAllTextAsync(IoPath.Combine(_projectFolder, "a.txt"), "", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(IoPath.Combine(otherFolder, "b.txt"), "", TestContext.Current.CancellationToken);

        using var sessionA = await FlowSession.OpenAsync(host, _projectFolder, TestContext.Current.CancellationToken);
        using var sessionB = await FlowSession.OpenAsync(host, otherFolder, TestContext.Current.CancellationToken);
        var hierarchyClient = sessionA.Hierarchy;
        var contextClient = sessionA.Context;
        var headers = sessionA.Headers;
        var projectA = sessionA.ProjectId;
        var projectB = sessionB.ProjectId;
        var watchA = ShortGuid.NewShortGuid();
        var watchB = ShortGuid.NewShortGuid();
        var entryA = await EntryIdOfAsync(hierarchyClient, projectA, watchA, headers, "a.txt");
        var entryB = await EntryIdOfAsync(hierarchyClient, projectB, watchB, headers, "b.txt");

        using var cts = FlowSession.CreateMessageTimeout(TestContext.Current.CancellationToken);
        using var contextA = contextClient.Watch(new WatchContextRequest { ProjectId = projectA, WatchId = watchA }, headers, cancellationToken: TestContext.Current.CancellationToken);
        await StreamProbe.WaitUntilContextLiveAsync(contextA.ResponseStream, cts.Token);
        using var contextB = contextClient.Watch(new WatchContextRequest { ProjectId = projectB, WatchId = watchB }, headers, cancellationToken: TestContext.Current.CancellationToken);
        await StreamProbe.WaitUntilContextLiveAsync(contextB.ResponseStream, cts.Token);

        // Act and assert, step by step.
        await RenameViaContextAsync(contextClient, projectA, watchA, headers, entryA, contextA.ResponseStream, cts.Token, "a-renamed.txt");
        await RenameViaContextAsync(contextClient, projectB, watchB, headers, entryB, contextB.ResponseStream, cts.Token, "b-renamed.txt");
        Assert.True(File.Exists(IoPath.Combine(_projectFolder, "a-renamed.txt")));
        Assert.True(File.Exists(IoPath.Combine(otherFolder, "b-renamed.txt")));

        // Undo on project A only: A's rename reverses, B's is untouched - the histories are keyed
        // by root path, so this fails outright if anything were process-wide.
        var undone = await ExecuteProjectActionAsync(contextClient, projectA, watchA, headers, HistoryContextActionProvider.UndoActionId);
        Assert.True(undone.Accepted, undone.Error);

        Assert.True(await Eventually.TrueAsync(() => File.Exists(IoPath.Combine(_projectFolder, "a.txt")), TestContext.Current.CancellationToken, FlowConstants.MessageTimeout), "a.txt did not appear on disk within the timeout.");
        Assert.True(File.Exists(IoPath.Combine(_projectFolder, "a.txt")));
        Assert.True(File.Exists(IoPath.Combine(otherFolder, "b-renamed.txt")), "Undo in project A reversed a rename in project B.");
        Assert.False(File.Exists(IoPath.Combine(otherFolder, "b.txt")));
    }

    [Fact]
    public async Task TwoConnectionsInOneProject_ShareOneHistory_SoACommandOnAMakesUndoAvailableOnBAndBsUndoReachesA()
    {
        // Arrange.
        await File.WriteAllTextAsync(IoPath.Combine(_projectFolder, "shared.txt"), "content", TestContext.Current.CancellationToken);

        using var session = await FlowSession.OpenAsync(host, _projectFolder, TestContext.Current.CancellationToken);
        var hierarchyClient = session.Hierarchy;
        var contextClient = session.Context;
        var watchA = ShortGuid.NewShortGuid();
        var watchB = ShortGuid.NewShortGuid();
        var entryId = await EntryIdOfAsync(hierarchyClient, session.ProjectId, watchA, session.Headers, "shared.txt");

        using var cts = FlowSession.CreateMessageTimeout(TestContext.Current.CancellationToken);
        using var contextA = contextClient.Watch(new WatchContextRequest { ProjectId = session.ProjectId, WatchId = watchA }, session.Headers, cancellationToken: TestContext.Current.CancellationToken);
        await StreamProbe.WaitUntilContextLiveAsync(contextA.ResponseStream, cts.Token);
        using var contextB = contextClient.Watch(new WatchContextRequest { ProjectId = session.ProjectId, WatchId = watchB }, session.Headers, cancellationToken: TestContext.Current.CancellationToken);
        await StreamProbe.WaitUntilContextLiveAsync(contextB.ResponseStream, cts.Token);
        // B starts reading its stream so the server has its writer registered before A acts.
        var pendingUndoAvailableOnB = ReadUntilProjectActionsAsync(contextB.ResponseStream, cts.Token, undoAvailable: true);

        await RenameViaContextAsync(contextClient, session.ProjectId, watchA, session.Headers, entryId, contextA.ResponseStream, cts.Token, "shared-renamed.txt");

        // Act and assert, step by step.
        // The command on A made Undo available on B, though B selected nothing and did nothing.
        var onB = await pendingUndoAvailableOnB;
        Assert.True(UndoOf(onB).Available);

        // B undoes; A sees it - Undo goes back to unavailable on A's stream, and the file is back.
        var pendingUndoGoneOnA = ReadUntilProjectActionsAsync(contextA.ResponseStream, cts.Token, undoAvailable: false);
        var undone = await ExecuteProjectActionAsync(contextClient, session.ProjectId, watchB, session.Headers, HistoryContextActionProvider.UndoActionId);
        Assert.True(undone.Accepted, undone.Error);

        var onA = await pendingUndoGoneOnA;
        Assert.False(UndoOf(onA).Available);
        Assert.True(await Eventually.TrueAsync(() => File.Exists(IoPath.Combine(_projectFolder, "shared.txt")), TestContext.Current.CancellationToken, FlowConstants.MessageTimeout), "shared.txt did not appear on disk within the timeout.");
        Assert.False(File.Exists(IoPath.Combine(_projectFolder, "shared-renamed.txt")));
    }

    [Fact]
    public async Task DeletingAnEntry_RecordsNothing_SoUndoStillReversesTheRenameBeforeIt()
    {
        // Arrange.
        await File.WriteAllTextAsync(IoPath.Combine(_projectFolder, "keep.txt"), "content", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(IoPath.Combine(_projectFolder, "doomed.txt"), "", TestContext.Current.CancellationToken);

        using var session = await FlowSession.OpenAsync(host, _projectFolder, TestContext.Current.CancellationToken);
        var hierarchyClient = session.Hierarchy;
        var contextClient = session.Context;
        var watchId = ShortGuid.NewShortGuid();
        var keepId = await EntryIdOfAsync(hierarchyClient, session.ProjectId, watchId, session.Headers, "keep.txt");
        var doomedId = await EntryIdOfAsync(hierarchyClient, session.ProjectId, watchId, session.Headers, "doomed.txt");

        using var cts = FlowSession.CreateMessageTimeout(TestContext.Current.CancellationToken);
        using var contextCall = contextClient.Watch(new WatchContextRequest { ProjectId = session.ProjectId, WatchId = watchId }, session.Headers, cancellationToken: TestContext.Current.CancellationToken);
        await StreamProbe.WaitUntilContextLiveAsync(contextCall.ResponseStream, cts.Token);

        // Act and assert, step by step.
        // Rename keep.txt (recorded), then delete doomed.txt (a one-way action that records nothing).
        await RenameViaContextAsync(contextClient, session.ProjectId, watchId, session.Headers, keepId, contextCall.ResponseStream, cts.Token, "kept.txt");
        await DeleteViaContextAsync(contextClient, session.ProjectId, watchId, session.Headers, doomedId, contextCall.ResponseStream, cts.Token);
        Assert.False(File.Exists(IoPath.Combine(_projectFolder, "doomed.txt")));

        // Undo still has exactly the rename to reverse - the delete left nothing on the stack.
        var undone = await ExecuteProjectActionAsync(contextClient, session.ProjectId, watchId, session.Headers, HistoryContextActionProvider.UndoActionId);
        Assert.True(undone.Accepted, undone.Error);

        Assert.True(await Eventually.TrueAsync(() => File.Exists(IoPath.Combine(_projectFolder, "keep.txt")), TestContext.Current.CancellationToken, FlowConstants.MessageTimeout), "keep.txt did not appear on disk within the timeout.");
        Assert.False(File.Exists(IoPath.Combine(_projectFolder, "kept.txt")));
        Assert.False(File.Exists(IoPath.Combine(_projectFolder, "doomed.txt")), "Undo brought back a deleted file it never recorded.");

        // And nothing is left to undo: the rename was the only recorded command.
        var afterUndo = await ExecuteProjectActionAsync(contextClient, session.ProjectId, watchId, session.Headers, HistoryContextActionProvider.UndoActionId);
        Assert.False(afterUndo.Accepted);
    }

    // ---- helpers ----------------------------------------------------------------------

    private static async Task<ShortGuid> EntryIdOfAsync(
        HierarchyService.HierarchyServiceClient client, ShortGuid projectId, ShortGuid watchId, Metadata headers, string name)
    {
        var entries = await client.ListEntriesAsync(new ListEntriesRequest { ProjectId = projectId, WatchId = watchId }, headers, cancellationToken: TestContext.Current.CancellationToken);
        return entries.Entries.Entries_.Single(e => e.Name == name).Id;
    }

    /// <summary>Runs the rename arc through the context action: execute, read the prompt off this connection's stream, submit.</summary>
    private async Task RenameViaContextAsync(
        ContextService.ContextServiceClient contextClient, ShortGuid projectId, ShortGuid watchId, Metadata headers,
        ShortGuid entryId, IAsyncStreamReader<ContextMessage> contextStream, CancellationToken cancellationToken, string newName)
    {
        var interactionId = ShortGuid.NewShortGuid();
        var pendingPrompt = contextStream.ReadUntilPromptAsync(cancellationToken);

        var executed = await contextClient.ExecuteActionAsync(
            new ExecuteActionRequest
            {
                ProjectId = projectId,
                WatchId = watchId,
                Source = new ContextSource { EntryId = entryId },
                InteractionId = interactionId,
                ActionId = HierarchyContextActionProvider.RenameActionId,
            },
            headers,
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(executed.Accepted, executed.Error);

        var prompt = await pendingPrompt;
        Assert.Equal(ContextPrompt.PromptOneofCase.InputDialog, prompt.PromptCase);

        var submitted = await contextClient.SubmitInteractionAsync(
            new SubmitInteractionRequest { InteractionId = interactionId, Value = newName }, headers, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(submitted.Completed, submitted.Error);
    }

    /// <summary>Runs the delete arc: execute, read the confirm prompt, submit.</summary>
    private async Task DeleteViaContextAsync(
        ContextService.ContextServiceClient contextClient, ShortGuid projectId, ShortGuid watchId, Metadata headers,
        ShortGuid entryId, IAsyncStreamReader<ContextMessage> contextStream, CancellationToken cancellationToken)
    {
        var interactionId = ShortGuid.NewShortGuid();
        var pendingPrompt = contextStream.ReadUntilPromptAsync(cancellationToken);

        var executed = await contextClient.ExecuteActionAsync(
            new ExecuteActionRequest
            {
                ProjectId = projectId,
                WatchId = watchId,
                Source = new ContextSource { EntryId = entryId },
                InteractionId = interactionId,
                ActionId = HierarchyContextActionProvider.DeleteActionId,
            },
            headers,
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(executed.Accepted, executed.Error);

        var prompt = await pendingPrompt;
        Assert.Equal(ContextPrompt.PromptOneofCase.ConfirmDialog, prompt.PromptCase);

        var submitted = await contextClient.SubmitInteractionAsync(
            new SubmitInteractionRequest { InteractionId = interactionId, Value = "" }, headers, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(submitted.Completed, submitted.Error);
    }

    private static Task<ExecuteActionResponse> ExecuteProjectActionAsync(
        ContextService.ContextServiceClient contextClient, ShortGuid projectId, ShortGuid watchId, Metadata headers, string actionId) =>
        contextClient.ExecuteActionAsync(
            new ExecuteActionRequest
            {
                ProjectId = projectId,
                WatchId = watchId,
                Source = new ContextSource { Project = new Empty() },
                InteractionId = ShortGuid.NewShortGuid(),
                ActionId = actionId,
            },
            headers,
            cancellationToken: TestContext.Current.CancellationToken).ResponseAsync;

    /// <summary>
    /// Reads project-actions messages until one matches the wanted Undo availability (or the
    /// first, when none is asked for) - so a test can wait past the baseline for the push a
    /// command produced.
    /// </summary>
    private static async Task<ContextProjectActions> ReadUntilProjectActionsAsync(
        IAsyncStreamReader<ContextMessage> stream, CancellationToken cancellationToken, bool? undoAvailable = null)
    {
        while (await stream.MoveNext(cancellationToken))
        {
            if (stream.Current.MessageCase != ContextMessage.MessageOneofCase.ProjectActions)
            {
                continue;
            }

            var actions = stream.Current.ProjectActions;
            if (undoAvailable is null || UndoOf(actions).Available == undoAvailable)
            {
                return actions;
            }
        }

        throw new InvalidOperationException("The stream ended before the expected project-actions message arrived.");
    }

    private static ContextAction UndoOf(ContextProjectActions actions) =>
        actions.Actions.Actions().Single(action => action.Id == HistoryContextActionProvider.UndoActionId);
}
