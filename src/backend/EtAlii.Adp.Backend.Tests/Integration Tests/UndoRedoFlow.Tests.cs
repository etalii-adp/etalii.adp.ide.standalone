using EtAlii.Adp.Backend.Hierarchy;
using EtAlii.Adp.Backend.Projects;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;
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
public class UndoRedoFlowTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private const string DeveloperUsername = "admin";
    private const string DeveloperCredential = "changeme";
    private const string SessionTokenHeader = "session-token";
    private static readonly TimeSpan MessageTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan StreamStartupGrace = TimeSpan.FromMilliseconds(500);

    private readonly WebApplicationFactory<Program> _factory;
    private readonly string _appDataRoot;
    private readonly string _projectFolder;

    public UndoRedoFlowTests(WebApplicationFactory<Program> baseFactory)
    {
        _appDataRoot = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.IntegrationTests", Guid.NewGuid().ToString("N"));
        _projectFolder = IoPath.Combine(_appDataRoot, "sample-project");
        Directory.CreateDirectory(_projectFolder);

        _factory = baseFactory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("developer");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IProjectStore>();
                services.AddSingleton<IProjectStore>(new FileProjectStore(_appDataRoot));
                // The problem cache must live and die with this test, not in the real user
                // profile the host's AddProblems registration points at (found by the
                // errors-and-warnings-panel manual pass: every run left a cache file behind).
                services.RemoveAll<Problems.IProblemStore>();
                services.AddSingleton<Problems.IProblemStore>(provider => new Problems.ProblemStore(
                    _appDataRoot,
                    provider.GetRequiredService<DiagramFileRouter>(),
                    provider.GetRequiredService<Diagram.DiagramValidators>()));
            });
        });
    }

    public void Dispose()
    {
        _factory.Dispose();
        TestFolder.TryDelete(_appDataRoot);
    }

    // ---- scenarios --------------------------------------------------------------------

    [Fact]
    public async Task RenameThroughAContextAction_MakesUndoAvailable_AndUndoReversesItOnDiskAndOnTheStream()
    {
        // Arrange.
        await File.WriteAllTextAsync(IoPath.Combine(_projectFolder, "original.txt"), "content", TestContext.Current.CancellationToken);
        using var channel = CreateChannel();
        var hierarchyClient = new HierarchyService.HierarchyServiceClient(channel);
        var contextClient = new ContextService.ContextServiceClient(channel);
        var headers = await LoginAsync(channel);
        var projectId = await AddProjectAsync(channel, headers);
        var watchId = ShortGuid.NewShortGuid();
        var entryId = await EntryIdOfAsync(hierarchyClient, projectId, watchId, headers, "original.txt");

        using var hierarchyCall = hierarchyClient.WatchHierarchy(new WatchHierarchyRequest { ProjectId = projectId, WatchId = watchId }, headers, cancellationToken: TestContext.Current.CancellationToken);
        using var cts = CreateMessageTimeout();
        using var contextCall = contextClient.Watch(new WatchContextRequest { ProjectId = projectId, WatchId = watchId }, headers, cancellationToken: TestContext.Current.CancellationToken);

        // Act and assert, step by step.
        // Rename original.txt -> renamed.txt through the context action, then let the first
        // (forward) rename change drain off the hierarchy stream.
        var pendingForward = ReadUntilChangeAsync(hierarchyCall.ResponseStream, HierarchyChange.ChangeOneofCase.Renamed, cts.Token);
        await RenameViaContextAsync(contextClient, projectId, watchId, headers, entryId, contextCall.ResponseStream, cts.Token, "renamed.txt");
        await pendingForward;
        Assert.True(File.Exists(IoPath.Combine(_projectFolder, "renamed.txt")));

        // The rename changed the history, so Undo is pushed as available to this connection.
        var afterRename = await ReadUntilProjectActionsAsync(contextCall.ResponseStream, cts.Token, undoAvailable: true);
        Assert.True(UndoOf(afterRename).Available);

        // Undo through the project-scoped action, with no prompt: it completes at once.
        var pendingReverse = ReadUntilChangeAsync(hierarchyCall.ResponseStream, HierarchyChange.ChangeOneofCase.Renamed, cts.Token);
        var undone = await ExecuteProjectActionAsync(contextClient, projectId, watchId, headers, HistoryContextActionProvider.UndoActionId);
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
        var otherFolder = IoPath.Combine(_appDataRoot, "other-project");
        Directory.CreateDirectory(otherFolder);
        await File.WriteAllTextAsync(IoPath.Combine(_projectFolder, "a.txt"), "", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(IoPath.Combine(otherFolder, "b.txt"), "", TestContext.Current.CancellationToken);

        using var channel = CreateChannel();
        var hierarchyClient = new HierarchyService.HierarchyServiceClient(channel);
        var contextClient = new ContextService.ContextServiceClient(channel);
        var headers = await LoginAsync(channel);
        var projectA = await AddProjectAsync(channel, headers, _projectFolder);
        var projectB = await AddProjectAsync(channel, headers, otherFolder);
        var watchA = ShortGuid.NewShortGuid();
        var watchB = ShortGuid.NewShortGuid();
        var entryA = await EntryIdOfAsync(hierarchyClient, projectA, watchA, headers, "a.txt");
        var entryB = await EntryIdOfAsync(hierarchyClient, projectB, watchB, headers, "b.txt");

        using var cts = CreateMessageTimeout();
        using var contextA = contextClient.Watch(new WatchContextRequest { ProjectId = projectA, WatchId = watchA }, headers, cancellationToken: TestContext.Current.CancellationToken);
        using var contextB = contextClient.Watch(new WatchContextRequest { ProjectId = projectB, WatchId = watchB }, headers, cancellationToken: TestContext.Current.CancellationToken);

        // Act and assert, step by step.
        await RenameViaContextAsync(contextClient, projectA, watchA, headers, entryA, contextA.ResponseStream, cts.Token, "a-renamed.txt");
        await RenameViaContextAsync(contextClient, projectB, watchB, headers, entryB, contextB.ResponseStream, cts.Token, "b-renamed.txt");
        Assert.True(File.Exists(IoPath.Combine(_projectFolder, "a-renamed.txt")));
        Assert.True(File.Exists(IoPath.Combine(otherFolder, "b-renamed.txt")));

        // Undo on project A only: A's rename reverses, B's is untouched - the histories are keyed
        // by root path, so this fails outright if anything were process-wide.
        var undone = await ExecuteProjectActionAsync(contextClient, projectA, watchA, headers, HistoryContextActionProvider.UndoActionId);
        Assert.True(undone.Accepted, undone.Error);

        await WaitUntilAsync(() => File.Exists(IoPath.Combine(_projectFolder, "a.txt")));
        Assert.True(File.Exists(IoPath.Combine(_projectFolder, "a.txt")));
        Assert.True(File.Exists(IoPath.Combine(otherFolder, "b-renamed.txt")), "Undo in project A reversed a rename in project B.");
        Assert.False(File.Exists(IoPath.Combine(otherFolder, "b.txt")));
    }

    [Fact]
    public async Task TwoConnectionsInOneProject_ShareOneHistory_SoACommandOnAMakesUndoAvailableOnBAndBsUndoReachesA()
    {
        // Arrange.
        await File.WriteAllTextAsync(IoPath.Combine(_projectFolder, "shared.txt"), "content", TestContext.Current.CancellationToken);

        using var channel = CreateChannel();
        var hierarchyClient = new HierarchyService.HierarchyServiceClient(channel);
        var contextClient = new ContextService.ContextServiceClient(channel);
        var headers = await LoginAsync(channel);
        var projectId = await AddProjectAsync(channel, headers);
        var watchA = ShortGuid.NewShortGuid();
        var watchB = ShortGuid.NewShortGuid();
        var entryId = await EntryIdOfAsync(hierarchyClient, projectId, watchA, headers, "shared.txt");

        using var cts = CreateMessageTimeout();
        using var contextA = contextClient.Watch(new WatchContextRequest { ProjectId = projectId, WatchId = watchA }, headers, cancellationToken: TestContext.Current.CancellationToken);
        using var contextB = contextClient.Watch(new WatchContextRequest { ProjectId = projectId, WatchId = watchB }, headers, cancellationToken: TestContext.Current.CancellationToken);
        // B starts reading its stream so the server has its writer registered before A acts.
        var pendingUndoAvailableOnB = ReadUntilProjectActionsAsync(contextB.ResponseStream, cts.Token, undoAvailable: true);
        await Task.Delay(StreamStartupGrace, TestContext.Current.CancellationToken);

        await RenameViaContextAsync(contextClient, projectId, watchA, headers, entryId, contextA.ResponseStream, cts.Token, "shared-renamed.txt");

        // Act and assert, step by step.
        // The command on A made Undo available on B, though B selected nothing and did nothing.
        var onB = await pendingUndoAvailableOnB;
        Assert.True(UndoOf(onB).Available);

        // B undoes; A sees it - Undo goes back to unavailable on A's stream, and the file is back.
        var pendingUndoGoneOnA = ReadUntilProjectActionsAsync(contextA.ResponseStream, cts.Token, undoAvailable: false);
        var undone = await ExecuteProjectActionAsync(contextClient, projectId, watchB, headers, HistoryContextActionProvider.UndoActionId);
        Assert.True(undone.Accepted, undone.Error);

        var onA = await pendingUndoGoneOnA;
        Assert.False(UndoOf(onA).Available);
        await WaitUntilAsync(() => File.Exists(IoPath.Combine(_projectFolder, "shared.txt")));
        Assert.False(File.Exists(IoPath.Combine(_projectFolder, "shared-renamed.txt")));
    }

    [Fact]
    public async Task DeletingAnEntry_RecordsNothing_SoUndoStillReversesTheRenameBeforeIt()
    {
        // Arrange.
        await File.WriteAllTextAsync(IoPath.Combine(_projectFolder, "keep.txt"), "content", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(IoPath.Combine(_projectFolder, "doomed.txt"), "", TestContext.Current.CancellationToken);

        using var channel = CreateChannel();
        var hierarchyClient = new HierarchyService.HierarchyServiceClient(channel);
        var contextClient = new ContextService.ContextServiceClient(channel);
        var headers = await LoginAsync(channel);
        var projectId = await AddProjectAsync(channel, headers);
        var watchId = ShortGuid.NewShortGuid();
        var keepId = await EntryIdOfAsync(hierarchyClient, projectId, watchId, headers, "keep.txt");
        var doomedId = await EntryIdOfAsync(hierarchyClient, projectId, watchId, headers, "doomed.txt");

        using var cts = CreateMessageTimeout();
        using var contextCall = contextClient.Watch(new WatchContextRequest { ProjectId = projectId, WatchId = watchId }, headers, cancellationToken: TestContext.Current.CancellationToken);

        // Act and assert, step by step.
        // Rename keep.txt (recorded), then delete doomed.txt (a one-way action that records nothing).
        await RenameViaContextAsync(contextClient, projectId, watchId, headers, keepId, contextCall.ResponseStream, cts.Token, "kept.txt");
        await DeleteViaContextAsync(contextClient, projectId, watchId, headers, doomedId, contextCall.ResponseStream, cts.Token);
        Assert.False(File.Exists(IoPath.Combine(_projectFolder, "doomed.txt")));

        // Undo still has exactly the rename to reverse - the delete left nothing on the stack.
        var undone = await ExecuteProjectActionAsync(contextClient, projectId, watchId, headers, HistoryContextActionProvider.UndoActionId);
        Assert.True(undone.Accepted, undone.Error);

        await WaitUntilAsync(() => File.Exists(IoPath.Combine(_projectFolder, "keep.txt")));
        Assert.False(File.Exists(IoPath.Combine(_projectFolder, "kept.txt")));
        Assert.False(File.Exists(IoPath.Combine(_projectFolder, "doomed.txt")), "Undo brought back a deleted file it never recorded.");

        // And nothing is left to undo: the rename was the only recorded command.
        var afterUndo = await ExecuteProjectActionAsync(contextClient, projectId, watchId, headers, HistoryContextActionProvider.UndoActionId);
        Assert.False(afterUndo.Accepted);
    }

    // ---- helpers ----------------------------------------------------------------------

    private static CancellationTokenSource CreateMessageTimeout()
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cts.CancelAfter(MessageTimeout);
        return cts;
    }

    private GrpcChannel CreateChannel()
    {
        var httpClient = _factory.CreateDefaultClient();
        return GrpcChannel.ForAddress(httpClient.BaseAddress!, new GrpcChannelOptions { HttpClient = httpClient });
    }

    private static async Task<Metadata> LoginAsync(GrpcChannel channel)
    {
        var authClient = new AuthenticationService.AuthenticationServiceClient(channel);
        var response = await authClient.LoginAsync(new LoginRequest { Username = DeveloperUsername, Credential = DeveloperCredential }, cancellationToken: TestContext.Current.CancellationToken);
        return new Metadata { { SessionTokenHeader, response.Session.Value } };
    }

    private async Task<ShortGuid> AddProjectAsync(GrpcChannel channel, Metadata headers, string? folder = null)
    {
        var projectClient = new ProjectService.ProjectServiceClient(channel);
        var pathMessage = new Path();
        pathMessage.Segments.AddRange((folder ?? _projectFolder).Split(IoPath.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries));
        var response = await projectClient.AddProjectAsync(new AddProjectRequest { Path = pathMessage }, headers, cancellationToken: TestContext.Current.CancellationToken);
        return response.Added.Id;
    }

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
        var pendingPrompt = ReadUntilPromptAsync(contextStream, cancellationToken);
        await Task.Delay(StreamStartupGrace, TestContext.Current.CancellationToken);

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
        var pendingPrompt = ReadUntilPromptAsync(contextStream, cancellationToken);
        await Task.Delay(StreamStartupGrace, TestContext.Current.CancellationToken);

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

    private static ContextAction UndoOf(ContextProjectActions actions) =>
        actions.Actions.SelectMany(group => group.Actions).Single(action => action.Id == HistoryContextActionProvider.UndoActionId);

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + MessageTimeout;
        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20, TestContext.Current.CancellationToken);
        }

        Assert.True(condition(), "The awaited condition did not hold within the timeout.");
    }
}
