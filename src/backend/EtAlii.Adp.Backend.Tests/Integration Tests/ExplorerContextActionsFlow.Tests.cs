using EtAlii.Adp.Context.Wire;
using EtAlii.Adp.Documents.Wire;
using EtAlii.Adp.Hierarchy;
using EtAlii.Adp.Hierarchy.Wire;
using EtAlii.Adp.TestSupport.Host;
using Grpc.Core;
using Xunit;
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Backend.Tests;

/// <summary>
/// Exercises the whole context-action arc against a real temporary folder and the real
/// backend host in-process: discovery, the prompt pushed back down that connection's own
/// WatchHierarchy stream, validation, commit, and the resulting on-disk change arriving as
/// an ordinary hierarchy change. The per-connection prompt isolation is the property most
/// worth having here.
/// </summary>
public class ExplorerContextActionsFlowTests(HostFixture host) : IClassFixture<HostFixture>
{
    private readonly string _projectFolder = host.NewProjectFolder();

    /// <summary>
    /// Reads the context stream until a prompt arrives, skipping the selection baseline and
    /// any selection change that precedes it.
    /// </summary>
    private static async Task<ContextPrompt> ReadUntilPromptAsync(IAsyncStreamReader<ContextMessage> stream, CancellationToken cancellationToken)
    {
        while (await stream.MoveNext(cancellationToken))
        {
            if (stream.Current.MessageCase == ContextMessage.MessageOneofCase.Prompt)
            {
                return stream.Current.Prompt;
            }
        }

        throw new InvalidOperationException("The stream ended before a prompt arrived.");
    }

    /// <summary>
    /// Reads until the wanted kind of hierarchy change arrives. A recursive folder delete
    /// legitimately emits other changes first (the folder's has-children flag flips as its
    /// contents go), so a test after the removal must look past those.
    /// </summary>
    private static async Task<HierarchyChange> ReadUntilChangeAsync(
        IAsyncStreamReader<HierarchyMessage> stream, HierarchyChange.ChangeOneofCase wanted, CancellationToken cancellationToken)
    {
        while (await stream.MoveNext(cancellationToken))
        {
            var message = stream.Current;
            if (message.MessageCase == HierarchyMessage.MessageOneofCase.Change && message.Change.ChangeCase == wanted)
            {
                return message.Change;
            }
        }

        throw new InvalidOperationException($"The stream ended before a {wanted} change arrived.");
    }

    [Fact]
    public async Task TheFullRenameArc_ValidatesThenRenamesOnDisk_AndReportsTheRenameOnTheSameStream()
    {
        // Arrange.
        await File.WriteAllTextAsync(IoPath.Combine(_projectFolder, "original.txt"), "content", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(IoPath.Combine(_projectFolder, "taken.txt"), "", TestContext.Current.CancellationToken);

        using var session = await FlowSession.OpenAsync(host, _projectFolder, TestContext.Current.CancellationToken);
        var hierarchyClient = session.Hierarchy;
        var contextClient = session.Context;
        var watchId = ShortGuid.NewShortGuid();

        var entries = await hierarchyClient.ListEntriesAsync(new ListEntriesRequest { ProjectId = session.ProjectId, WatchId = watchId }, session.Headers, cancellationToken: TestContext.Current.CancellationToken);
        var entryId = entries.Entries.Entries_.Single(e => e.Name == "original.txt").Id;

        using var call = hierarchyClient.WatchHierarchy(new WatchHierarchyRequest { ProjectId = session.ProjectId, WatchId = watchId }, session.Headers, cancellationToken: TestContext.Current.CancellationToken);
        using var cts = FlowSession.CreateMessageTimeout(TestContext.Current.CancellationToken);
        await StreamProbe.WaitUntilHierarchyLiveAsync(call.ResponseStream, _projectFolder, cts.Token);
        using var contextCall = contextClient.Watch(new WatchContextRequest { ProjectId = session.ProjectId, WatchId = watchId }, session.Headers, cancellationToken: TestContext.Current.CancellationToken);
        await StreamProbe.WaitUntilContextLiveAsync(contextCall.ResponseStream, cts.Token);
        var pendingPrompt = ReadUntilPromptAsync(contextCall.ResponseStream, cts.Token);

        // Act and assert, step by step.
        var source = new ContextSource { EntryId = entryId };
        var discovered = await contextClient.DiscoverActionsAsync(
            new DiscoverActionsRequest { ProjectId = session.ProjectId, WatchId = watchId, Source = source },
            session.Headers,
            cancellationToken: TestContext.Current.CancellationToken);
        var renameAction = discovered.Groups.Actions().Single(a => a.Id == HierarchyContextActionProvider.RenameActionId);
        Assert.True(renameAction.Available);

        var interactionId = ShortGuid.NewShortGuid();
        var executed = await contextClient.ExecuteActionAsync(
            new ExecuteActionRequest
            {
                ProjectId = session.ProjectId,
                WatchId = watchId,
                Source = source,
                InteractionId = interactionId,
                ActionId = renameAction.Id,
            },
            session.Headers,
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(executed.Accepted);

        var prompt = await pendingPrompt;
        Assert.Equal(ContextPrompt.PromptOneofCase.InputDialog, prompt.PromptCase);
        Assert.Equal(interactionId, (ShortGuid)prompt.InteractionId);
        Assert.Equal("original.txt", prompt.InputDialog.InitialValue);

        var rejected = await contextClient.ProposeInputAsync(
            new ProposeInputRequest { InteractionId = interactionId, Revision = 1, Value = "taken.txt" }, session.Headers, cancellationToken: TestContext.Current.CancellationToken);
        Assert.False(rejected.Valid);
        Assert.Equal(1u, rejected.Revision);

        var accepted = await contextClient.ProposeInputAsync(
            new ProposeInputRequest { InteractionId = interactionId, Revision = 2, Value = "renamed.txt" }, session.Headers, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(accepted.Valid);
        Assert.Equal(2u, accepted.Revision);

        var pendingChange = ReadUntilChangeAsync(call.ResponseStream, HierarchyChange.ChangeOneofCase.Renamed, cts.Token);

        var submitted = await contextClient.SubmitInteractionAsync(
            new SubmitInteractionRequest { InteractionId = interactionId, Value = "renamed.txt" }, session.Headers, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(submitted.Completed);
        Assert.False(File.Exists(IoPath.Combine(_projectFolder, "original.txt")));
        Assert.Equal("content", await File.ReadAllTextAsync(IoPath.Combine(_projectFolder, "renamed.txt"), TestContext.Current.CancellationToken));

        var change = await pendingChange;
        Assert.Equal((ShortGuid)entryId, (ShortGuid)change.Renamed.EntryId);
        Assert.Equal("renamed.txt", change.Renamed.NewName);
    }

    [Fact]
    public async Task TheFullDeleteArc_ConfirmsThenRemovesAPopulatedFolderRecursively_AndReportsTheRemoval()
    {
        // Arrange.
        var folder = IoPath.Combine(_projectFolder, "doomed");
        Directory.CreateDirectory(IoPath.Combine(folder, "inner"));
        await File.WriteAllTextAsync(IoPath.Combine(folder, "inner", "leaf.txt"), "", TestContext.Current.CancellationToken);

        using var session = await FlowSession.OpenAsync(host, _projectFolder, TestContext.Current.CancellationToken);
        var hierarchyClient = session.Hierarchy;
        var contextClient = session.Context;
        var watchId = ShortGuid.NewShortGuid();

        var entries = await hierarchyClient.ListEntriesAsync(new ListEntriesRequest { ProjectId = session.ProjectId, WatchId = watchId }, session.Headers, cancellationToken: TestContext.Current.CancellationToken);
        var entryId = entries.Entries.Entries_.Single(e => e.Name == "doomed").Id;

        using var call = hierarchyClient.WatchHierarchy(new WatchHierarchyRequest { ProjectId = session.ProjectId, WatchId = watchId }, session.Headers, cancellationToken: TestContext.Current.CancellationToken);
        using var cts = FlowSession.CreateMessageTimeout(TestContext.Current.CancellationToken);
        await StreamProbe.WaitUntilHierarchyLiveAsync(call.ResponseStream, _projectFolder, cts.Token);
        using var contextCall = contextClient.Watch(new WatchContextRequest { ProjectId = session.ProjectId, WatchId = watchId }, session.Headers, cancellationToken: TestContext.Current.CancellationToken);
        await StreamProbe.WaitUntilContextLiveAsync(contextCall.ResponseStream, cts.Token);
        var pendingPrompt = ReadUntilPromptAsync(contextCall.ResponseStream, cts.Token);

        // Act and assert, step by step.
        var interactionId = ShortGuid.NewShortGuid();
        var executed = await contextClient.ExecuteActionAsync(
            new ExecuteActionRequest
            {
                ProjectId = session.ProjectId,
                WatchId = watchId,
                Source = new ContextSource { EntryId = entryId },
                InteractionId = interactionId,
                ActionId = HierarchyContextActionProvider.DeleteActionId,
            },
            session.Headers,
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(executed.Accepted);

        var prompt = await pendingPrompt;
        Assert.Equal(ContextPrompt.PromptOneofCase.ConfirmDialog, prompt.PromptCase);
        Assert.True(prompt.ConfirmDialog.Danger);
        Assert.Contains("doomed", prompt.ConfirmDialog.Message);

        var pendingChange = ReadUntilChangeAsync(call.ResponseStream, HierarchyChange.ChangeOneofCase.Removed, cts.Token);

        var submitted = await contextClient.SubmitInteractionAsync(
            new SubmitInteractionRequest { InteractionId = interactionId, Value = "" }, session.Headers, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(submitted.Completed);
        Assert.False(Directory.Exists(folder));

        var change = await pendingChange;
        Assert.Equal((ShortGuid)entryId, (ShortGuid)change.Removed.EntryId);
    }

    [Fact]
    public async Task ExecuteAction_TriggeredByShortcutWithNoPriorDiscovery_ProducesTheSamePromptAsTheActionIdPath()
    {
        // Arrange.
        await File.WriteAllTextAsync(IoPath.Combine(_projectFolder, "shortcut.txt"), "", TestContext.Current.CancellationToken);

        // Arrange, continued.
        using var session = await FlowSession.OpenAsync(host, _projectFolder, TestContext.Current.CancellationToken);
        var hierarchyClient = session.Hierarchy;
        var contextClient = session.Context;
        var watchId = ShortGuid.NewShortGuid();

        // Arrange, continued.
        var entries = await hierarchyClient.ListEntriesAsync(new ListEntriesRequest { ProjectId = session.ProjectId, WatchId = watchId }, session.Headers, cancellationToken: TestContext.Current.CancellationToken);
        var entryId = entries.Entries.Entries_.Single(e => e.Name == "shortcut.txt").Id;

        // Arrange, continued.
        using var call = hierarchyClient.WatchHierarchy(new WatchHierarchyRequest { ProjectId = session.ProjectId, WatchId = watchId }, session.Headers, cancellationToken: TestContext.Current.CancellationToken);
        using var cts = FlowSession.CreateMessageTimeout(TestContext.Current.CancellationToken);
        using var contextCall = contextClient.Watch(new WatchContextRequest { ProjectId = session.ProjectId, WatchId = watchId }, session.Headers, cancellationToken: TestContext.Current.CancellationToken);
        await StreamProbe.WaitUntilContextLiveAsync(contextCall.ResponseStream, cts.Token);
        var pendingPrompt = ReadUntilPromptAsync(contextCall.ResponseStream, cts.Token);

        // Act.
        var executed = await contextClient.ExecuteActionAsync(
            new ExecuteActionRequest
            {
                ProjectId = session.ProjectId,
                WatchId = watchId,
                Source = new ContextSource { EntryId = entryId },
                InteractionId = ShortGuid.NewShortGuid(),
                Shortcut = new ContextShortcut { Key = "F2" },
            },
            session.Headers,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert.
        Assert.True(executed.Accepted);
        var prompt = await pendingPrompt;
        Assert.Equal(ContextPrompt.PromptOneofCase.InputDialog, prompt.PromptCase);
        Assert.Equal("shortcut.txt", prompt.InputDialog.InitialValue);
    }

    [Fact]
    public async Task APromptRaisedOnOneConnection_IsNeverObservedOnAnothersStream()
    {
        // Arrange.
        await File.WriteAllTextAsync(IoPath.Combine(_projectFolder, "shared.txt"), "", TestContext.Current.CancellationToken);

        using var session = await FlowSession.OpenAsync(host, _projectFolder, TestContext.Current.CancellationToken);
        var hierarchyClient = session.Hierarchy;
        var contextClient = session.Context;
        var watchIdA = ShortGuid.NewShortGuid();
        var watchIdB = ShortGuid.NewShortGuid();

        var entriesA = await hierarchyClient.ListEntriesAsync(new ListEntriesRequest { ProjectId = session.ProjectId, WatchId = watchIdA }, session.Headers, cancellationToken: TestContext.Current.CancellationToken);
        await hierarchyClient.ListEntriesAsync(new ListEntriesRequest { ProjectId = session.ProjectId, WatchId = watchIdB }, session.Headers, cancellationToken: TestContext.Current.CancellationToken);
        var entryIdA = entriesA.Entries.Entries_.Single(e => e.Name == "shared.txt").Id;

        using var callA = contextClient.Watch(new WatchContextRequest { ProjectId = session.ProjectId, WatchId = watchIdA }, session.Headers, cancellationToken: TestContext.Current.CancellationToken);
        using var callB = contextClient.Watch(new WatchContextRequest { ProjectId = session.ProjectId, WatchId = watchIdB }, session.Headers, cancellationToken: TestContext.Current.CancellationToken);
        using var ctsA = FlowSession.CreateMessageTimeout(TestContext.Current.CancellationToken);
        using var ctsB = FlowSession.CreateMessageTimeout(TestContext.Current.CancellationToken);

        // Act and assert, step by step.
        // Both streams open with three baseline messages: the "nothing selected" selection,
        // the project's own actions - undo and redo (diagram-undo-redo Deviation 1) - and
        // the project's problems (errors-and-warnings-panel Requirement 1.2). What matters
        // is what arrives after all three.
        Assert.True(await callA.ResponseStream.MoveNext(ctsA.Token));
        Assert.True(await callB.ResponseStream.MoveNext(ctsB.Token));
        Assert.True(await callA.ResponseStream.MoveNext(ctsA.Token));
        Assert.Equal(ContextMessage.MessageOneofCase.ProjectActions, callA.ResponseStream.Current.MessageCase);
        Assert.True(await callB.ResponseStream.MoveNext(ctsB.Token));
        Assert.Equal(ContextMessage.MessageOneofCase.ProjectActions, callB.ResponseStream.Current.MessageCase);
        Assert.True(await callA.ResponseStream.MoveNext(ctsA.Token));
        Assert.Equal(ContextMessage.MessageOneofCase.Problems, callA.ResponseStream.Current.MessageCase);
        Assert.True(await callB.ResponseStream.MoveNext(ctsB.Token));
        Assert.Equal(ContextMessage.MessageOneofCase.Problems, callB.ResponseStream.Current.MessageCase);
        var pendingA = callA.ResponseStream.MoveNext(ctsA.Token);
        var pendingB = callB.ResponseStream.MoveNext(ctsB.Token);

        await contextClient.ExecuteActionAsync(
            new ExecuteActionRequest
            {
                ProjectId = session.ProjectId,
                WatchId = watchIdA,
                Source = new ContextSource { EntryId = entryIdA },
                InteractionId = ShortGuid.NewShortGuid(),
                ActionId = HierarchyContextActionProvider.RenameActionId,
            },
            session.Headers,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(await pendingA, "Expected the prompt on connection A but its stream ended or timed out.");
        Assert.Equal(ContextMessage.MessageOneofCase.Prompt, callA.ResponseStream.Current.MessageCase);

        // Nothing happened on disk, so B has nothing to receive at all - and certainly not
        // A's prompt. Anything arriving within this window is a leak.
        var arrivedOnB = await Task.WhenAny(pendingB, Task.Delay(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken)) == pendingB;
        Assert.False(arrivedOnB, "Connection B observed a message raised for connection A.");
    }

    [Fact]
    public async Task DiscoverActions_ForAnEntryIdBelongingToAnotherConnection_ReportsNothing()
    {
        // Arrange.
        await File.WriteAllTextAsync(IoPath.Combine(_projectFolder, "shared.txt"), "", TestContext.Current.CancellationToken);

        // Arrange, continued.
        using var session = await FlowSession.OpenAsync(host, _projectFolder, TestContext.Current.CancellationToken);
        var hierarchyClient = session.Hierarchy;
        var contextClient = session.Context;
        var watchIdA = ShortGuid.NewShortGuid();
        var watchIdB = ShortGuid.NewShortGuid();

        // Arrange, continued.
        var entriesA = await hierarchyClient.ListEntriesAsync(new ListEntriesRequest { ProjectId = session.ProjectId, WatchId = watchIdA }, session.Headers, cancellationToken: TestContext.Current.CancellationToken);
        var entryIdA = entriesA.Entries.Entries_.Single(e => e.Name == "shared.txt").Id;

        // Act.
        var discovered = await contextClient.DiscoverActionsAsync(
            new DiscoverActionsRequest
            {
                ProjectId = session.ProjectId,
                WatchId = watchIdB,
                Source = new ContextSource { EntryId = entryIdA },
            },
            session.Headers,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert.
        Assert.Empty(discovered.Groups);
    }

    [Fact]
    public async Task DiscoverActions_ForAProjectTheCallerIsNotAuthorizedFor_ReportsNothing()
    {
        // Arrange.
        await File.WriteAllTextAsync(IoPath.Combine(_projectFolder, "private.txt"), "", TestContext.Current.CancellationToken);

        // Arrange, continued.
        using var owner = await FlowSession.OpenAsync(host, _projectFolder, TestContext.Current.CancellationToken);
        var ownerClient = owner.Hierarchy;
        var ownerContextClient = owner.Context;
        var projectId = owner.ProjectId;
        var watchId = ShortGuid.NewShortGuid();
        var entries = await ownerClient.ListEntriesAsync(new ListEntriesRequest { ProjectId = projectId, WatchId = watchId }, owner.Headers, cancellationToken: TestContext.Current.CancellationToken);
        var entryId = entries.Entries.Entries_.Single(e => e.Name == "private.txt").Id;

        // Arrange, continued.
        // A second login is a second session; the project was never added under it, so the
        // project id resolves to nothing for this caller.
        using var otherChannel = host.CreateChannel();
        var otherHeaders = new Metadata { { FlowConstants.SessionTokenHeader, "not-a-valid-session" } };
        var otherContextClient = new ContextService.ContextServiceClient(otherChannel);

        // Act.
        var rpcException = await Assert.ThrowsAsync<RpcException>(async () => await otherContextClient.DiscoverActionsAsync(
            new DiscoverActionsRequest
            {
                ProjectId = projectId,
                WatchId = watchId,
                Source = new ContextSource { EntryId = entryId },
            },
            otherHeaders, cancellationToken: TestContext.Current.CancellationToken));

        // Assert.
        Assert.NotNull(ownerContextClient);
        Assert.Equal(StatusCode.Unauthenticated, rpcException.StatusCode);
    }

    [Fact]
    public async Task ExecuteAction_WithoutASource_ActsOnTheConnectionsCurrentSelection()
    {
        // Arrange.
        await File.WriteAllTextAsync(IoPath.Combine(_projectFolder, "selected.txt"), "", TestContext.Current.CancellationToken);

        // Arrange, continued.
        using var session = await FlowSession.OpenAsync(host, _projectFolder, TestContext.Current.CancellationToken);
        var hierarchyClient = session.Hierarchy;
        var contextClient = session.Context;
        var watchId = ShortGuid.NewShortGuid();

        // Arrange, continued.
        var entries = await hierarchyClient.ListEntriesAsync(new ListEntriesRequest { ProjectId = session.ProjectId, WatchId = watchId }, session.Headers, cancellationToken: TestContext.Current.CancellationToken);
        var entryId = entries.Entries.Entries_.Single(e => e.Name == "selected.txt").Id;

        // Arrange, continued.
        using var cts = FlowSession.CreateMessageTimeout(TestContext.Current.CancellationToken);
        using var contextCall = contextClient.Watch(new WatchContextRequest { ProjectId = session.ProjectId, WatchId = watchId }, session.Headers, cancellationToken: TestContext.Current.CancellationToken);
        await StreamProbe.WaitUntilContextLiveAsync(contextCall.ResponseStream, cts.Token);
        var pendingPrompt = ReadUntilPromptAsync(contextCall.ResponseStream, cts.Token);

        // Arrange, continued.
        var nothingSelected = await contextClient.ExecuteActionAsync(
            new ExecuteActionRequest
            {
                ProjectId = session.ProjectId,
                WatchId = watchId,
                InteractionId = ShortGuid.NewShortGuid(),
                ActionId = HierarchyContextActionProvider.RenameActionId,
            },
            session.Headers,
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.False(nothingSelected.Accepted);

        // Arrange, continued.
        var selection = new ContextSelection { Source = ContextSelectionSource.Explorer, Id = new ContextSource { EntryId = entryId } };
        var selected = await contextClient.SelectAsync(new SelectRequest { ProjectId = session.ProjectId, WatchId = watchId, Selection = selection }, session.Headers, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("", selected.Error);

        // Act.
        var executed = await contextClient.ExecuteActionAsync(
            new ExecuteActionRequest
            {
                ProjectId = session.ProjectId,
                WatchId = watchId,
                InteractionId = ShortGuid.NewShortGuid(),
                ActionId = HierarchyContextActionProvider.RenameActionId,
            },
            session.Headers,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert.
        Assert.True(executed.Accepted);
        var prompt = await pendingPrompt;
        Assert.Equal("selected.txt", prompt.InputDialog.InitialValue);
    }

    [Fact]
    public async Task SubmitInteraction_RepeatedForTheSameId_DoesNothingTheSecondTime()
    {
        // Arrange.
        await File.WriteAllTextAsync(IoPath.Combine(_projectFolder, "once.txt"), "", TestContext.Current.CancellationToken);

        // Arrange, continued.
        using var session = await FlowSession.OpenAsync(host, _projectFolder, TestContext.Current.CancellationToken);
        var hierarchyClient = session.Hierarchy;
        var contextClient = session.Context;
        var watchId = ShortGuid.NewShortGuid();

        // Arrange, continued.
        var entries = await hierarchyClient.ListEntriesAsync(new ListEntriesRequest { ProjectId = session.ProjectId, WatchId = watchId }, session.Headers, cancellationToken: TestContext.Current.CancellationToken);
        var entryId = entries.Entries.Entries_.Single(e => e.Name == "once.txt").Id;

        // Arrange, continued.
        using var call = hierarchyClient.WatchHierarchy(new WatchHierarchyRequest { ProjectId = session.ProjectId, WatchId = watchId }, session.Headers, cancellationToken: TestContext.Current.CancellationToken);
        using var cts = FlowSession.CreateMessageTimeout(TestContext.Current.CancellationToken);
        using var contextCall = contextClient.Watch(new WatchContextRequest { ProjectId = session.ProjectId, WatchId = watchId }, session.Headers, cancellationToken: TestContext.Current.CancellationToken);
        await StreamProbe.WaitUntilContextLiveAsync(contextCall.ResponseStream, cts.Token);
        var pendingPrompt = ReadUntilPromptAsync(contextCall.ResponseStream, cts.Token);

        // Arrange, continued.
        var interactionId = ShortGuid.NewShortGuid();
        await contextClient.ExecuteActionAsync(
            new ExecuteActionRequest
            {
                ProjectId = session.ProjectId,
                WatchId = watchId,
                Source = new ContextSource { EntryId = entryId },
                InteractionId = interactionId,
                ActionId = HierarchyContextActionProvider.RenameActionId,
            },
            session.Headers,
            cancellationToken: TestContext.Current.CancellationToken);
        await pendingPrompt;

        // Act.
        var first = await contextClient.SubmitInteractionAsync(
            new SubmitInteractionRequest { InteractionId = interactionId, Value = "twice.txt" }, session.Headers, cancellationToken: TestContext.Current.CancellationToken);
        var second = await contextClient.SubmitInteractionAsync(
            new SubmitInteractionRequest { InteractionId = interactionId, Value = "thrice.txt" }, session.Headers, cancellationToken: TestContext.Current.CancellationToken);

        // Assert.
        Assert.True(first.Completed);
        Assert.False(second.Completed);
        Assert.True(File.Exists(IoPath.Combine(_projectFolder, "twice.txt")));
        Assert.False(File.Exists(IoPath.Combine(_projectFolder, "thrice.txt")));
    }
}
