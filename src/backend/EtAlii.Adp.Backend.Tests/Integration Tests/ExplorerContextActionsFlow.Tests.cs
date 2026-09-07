using EtAlii.Adp.Authentication.Wire;
using EtAlii.Adp.Common.Wire;
using EtAlii.Adp.Context.Wire;
using EtAlii.Adp.Hierarchy;
using EtAlii.Adp.Hierarchy.Wire;
using EtAlii.Adp.Projects;
using EtAlii.Adp.Projects.Wire;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;
using HierarchyService = EtAlii.Adp.Hierarchy.Wire.HierarchyService;
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here
using Path = EtAlii.Adp.Common.Wire.Path;
using ProjectService = EtAlii.Adp.Projects.Wire.ProjectService;

namespace EtAlii.Adp.Backend.Tests;

/// <summary>
/// Exercises the whole context-action arc against a real temporary folder and the real
/// backend host in-process: discovery, the prompt pushed back down that connection's own
/// WatchHierarchy stream, validation, commit, and the resulting on-disk change arriving as
/// an ordinary hierarchy change. The per-connection prompt isolation is the property most
/// worth having here.
/// </summary>
public class ExplorerContextActionsFlowTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private const string DeveloperUsername = "admin";
    private const string DeveloperCredential = "changeme";
    private const string SessionTokenHeader = "session-token";
    private static readonly TimeSpan MessageTimeout = TimeSpan.FromSeconds(10);

    // Grpc.Net.Client only invokes a server-streaming method on the first read, so every
    // test starts reading (MoveNext, not awaited) before triggering anything, then gives
    // the server this long to get as far as registering its prompt writer.
    private static readonly TimeSpan StreamStartupGrace = TimeSpan.FromMilliseconds(500);

    private readonly WebApplicationFactory<Program> _factory;
    private readonly string _appDataRoot;
    private readonly string _projectFolder;

    public ExplorerContextActionsFlowTests(WebApplicationFactory<Program> baseFactory)
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
                    provider.GetRequiredService<Common.DiagramValidators>()));
            });
        });
    }

    public void Dispose()
    {
        _factory.Dispose();
        TestFolder.TryDelete(_appDataRoot);
    }

    /// <summary>
    /// The per-message timeout, linked to the test's own cancellation token so a stream that
    /// never delivers gives up as soon as the test is cancelled rather than waiting it out.
    /// </summary>
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

        using var channel = CreateChannel();
        var hierarchyClient = new HierarchyService.HierarchyServiceClient(channel);
        var contextClient = new ContextService.ContextServiceClient(channel);
        var headers = await LoginAsync(channel);
        var projectId = await AddProjectAsync(channel, headers);
        var watchId = ShortGuid.NewShortGuid();

        var entries = await hierarchyClient.ListEntriesAsync(new ListEntriesRequest { ProjectId = projectId, WatchId = watchId }, headers, cancellationToken: TestContext.Current.CancellationToken);
        var entryId = entries.Entries.Entries_.Single(e => e.Name == "original.txt").Id;

        using var call = hierarchyClient.WatchHierarchy(new WatchHierarchyRequest { ProjectId = projectId, WatchId = watchId }, headers, cancellationToken: TestContext.Current.CancellationToken);
        using var cts = CreateMessageTimeout();
        using var contextCall = contextClient.Watch(new WatchContextRequest { ProjectId = projectId, WatchId = watchId }, headers, cancellationToken: TestContext.Current.CancellationToken);
        var pendingPrompt = ReadUntilPromptAsync(contextCall.ResponseStream, cts.Token);
        await Task.Delay(StreamStartupGrace, TestContext.Current.CancellationToken);

        // Act and assert, step by step.
        var source = new ContextSource { EntryId = entryId };
        var discovered = await contextClient.DiscoverActionsAsync(
            new DiscoverActionsRequest { ProjectId = projectId, WatchId = watchId, Source = source },
            headers,
            cancellationToken: TestContext.Current.CancellationToken);
        var renameAction = discovered.Groups.SelectMany(g => g.Actions).Single(a => a.Id == HierarchyContextActionProvider.RenameActionId);
        Assert.True(renameAction.Available);

        var interactionId = ShortGuid.NewShortGuid();
        var executed = await contextClient.ExecuteActionAsync(
            new ExecuteActionRequest
            {
                ProjectId = projectId,
                WatchId = watchId,
                Source = source,
                InteractionId = interactionId,
                ActionId = renameAction.Id,
            },
            headers,
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(executed.Accepted);

        var prompt = await pendingPrompt;
        Assert.Equal(ContextPrompt.PromptOneofCase.InputDialog, prompt.PromptCase);
        Assert.Equal(interactionId, (ShortGuid)prompt.InteractionId);
        Assert.Equal("original.txt", prompt.InputDialog.InitialValue);

        var rejected = await contextClient.ProposeInputAsync(
            new ProposeInputRequest { InteractionId = interactionId, Revision = 1, Value = "taken.txt" }, headers, cancellationToken: TestContext.Current.CancellationToken);
        Assert.False(rejected.Valid);
        Assert.Equal(1u, rejected.Revision);

        var accepted = await contextClient.ProposeInputAsync(
            new ProposeInputRequest { InteractionId = interactionId, Revision = 2, Value = "renamed.txt" }, headers, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(accepted.Valid);
        Assert.Equal(2u, accepted.Revision);

        var pendingChange = ReadUntilChangeAsync(call.ResponseStream, HierarchyChange.ChangeOneofCase.Renamed, cts.Token);

        var submitted = await contextClient.SubmitInteractionAsync(
            new SubmitInteractionRequest { InteractionId = interactionId, Value = "renamed.txt" }, headers, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(submitted.Completed);
        Assert.False(File.Exists(IoPath.Combine(_projectFolder, "original.txt")));
        Assert.Equal("content", File.ReadAllText(IoPath.Combine(_projectFolder, "renamed.txt")));

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

        using var channel = CreateChannel();
        var hierarchyClient = new HierarchyService.HierarchyServiceClient(channel);
        var contextClient = new ContextService.ContextServiceClient(channel);
        var headers = await LoginAsync(channel);
        var projectId = await AddProjectAsync(channel, headers);
        var watchId = ShortGuid.NewShortGuid();

        var entries = await hierarchyClient.ListEntriesAsync(new ListEntriesRequest { ProjectId = projectId, WatchId = watchId }, headers, cancellationToken: TestContext.Current.CancellationToken);
        var entryId = entries.Entries.Entries_.Single(e => e.Name == "doomed").Id;

        using var call = hierarchyClient.WatchHierarchy(new WatchHierarchyRequest { ProjectId = projectId, WatchId = watchId }, headers, cancellationToken: TestContext.Current.CancellationToken);
        using var cts = CreateMessageTimeout();
        using var contextCall = contextClient.Watch(new WatchContextRequest { ProjectId = projectId, WatchId = watchId }, headers, cancellationToken: TestContext.Current.CancellationToken);
        var pendingPrompt = ReadUntilPromptAsync(contextCall.ResponseStream, cts.Token);
        await Task.Delay(StreamStartupGrace, TestContext.Current.CancellationToken);

        // Act and assert, step by step.
        var interactionId = ShortGuid.NewShortGuid();
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
        Assert.True(executed.Accepted);

        var prompt = await pendingPrompt;
        Assert.Equal(ContextPrompt.PromptOneofCase.ConfirmDialog, prompt.PromptCase);
        Assert.True(prompt.ConfirmDialog.Danger);
        Assert.Contains("doomed", prompt.ConfirmDialog.Message);

        var pendingChange = ReadUntilChangeAsync(call.ResponseStream, HierarchyChange.ChangeOneofCase.Removed, cts.Token);

        var submitted = await contextClient.SubmitInteractionAsync(
            new SubmitInteractionRequest { InteractionId = interactionId, Value = "" }, headers, cancellationToken: TestContext.Current.CancellationToken);
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
        using var channel = CreateChannel();
        var hierarchyClient = new HierarchyService.HierarchyServiceClient(channel);
        var contextClient = new ContextService.ContextServiceClient(channel);
        var headers = await LoginAsync(channel);
        var projectId = await AddProjectAsync(channel, headers);
        var watchId = ShortGuid.NewShortGuid();

        // Arrange, continued.
        var entries = await hierarchyClient.ListEntriesAsync(new ListEntriesRequest { ProjectId = projectId, WatchId = watchId }, headers, cancellationToken: TestContext.Current.CancellationToken);
        var entryId = entries.Entries.Entries_.Single(e => e.Name == "shortcut.txt").Id;

        // Arrange, continued.
        using var call = hierarchyClient.WatchHierarchy(new WatchHierarchyRequest { ProjectId = projectId, WatchId = watchId }, headers, cancellationToken: TestContext.Current.CancellationToken);
        using var cts = CreateMessageTimeout();
        using var contextCall = contextClient.Watch(new WatchContextRequest { ProjectId = projectId, WatchId = watchId }, headers, cancellationToken: TestContext.Current.CancellationToken);
        var pendingPrompt = ReadUntilPromptAsync(contextCall.ResponseStream, cts.Token);
        await Task.Delay(StreamStartupGrace, TestContext.Current.CancellationToken);

        // Act.
        var executed = await contextClient.ExecuteActionAsync(
            new ExecuteActionRequest
            {
                ProjectId = projectId,
                WatchId = watchId,
                Source = new ContextSource { EntryId = entryId },
                InteractionId = ShortGuid.NewShortGuid(),
                Shortcut = new ContextShortcut { Key = "F2" },
            },
            headers,
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

        using var channel = CreateChannel();
        var hierarchyClient = new HierarchyService.HierarchyServiceClient(channel);
        var contextClient = new ContextService.ContextServiceClient(channel);
        var headers = await LoginAsync(channel);
        var projectId = await AddProjectAsync(channel, headers);
        var watchIdA = ShortGuid.NewShortGuid();
        var watchIdB = ShortGuid.NewShortGuid();

        var entriesA = await hierarchyClient.ListEntriesAsync(new ListEntriesRequest { ProjectId = projectId, WatchId = watchIdA }, headers, cancellationToken: TestContext.Current.CancellationToken);
        await hierarchyClient.ListEntriesAsync(new ListEntriesRequest { ProjectId = projectId, WatchId = watchIdB }, headers, cancellationToken: TestContext.Current.CancellationToken);
        var entryIdA = entriesA.Entries.Entries_.Single(e => e.Name == "shared.txt").Id;

        using var callA = contextClient.Watch(new WatchContextRequest { ProjectId = projectId, WatchId = watchIdA }, headers, cancellationToken: TestContext.Current.CancellationToken);
        using var callB = contextClient.Watch(new WatchContextRequest { ProjectId = projectId, WatchId = watchIdB }, headers, cancellationToken: TestContext.Current.CancellationToken);
        using var ctsA = CreateMessageTimeout();
        using var ctsB = CreateMessageTimeout();

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
                ProjectId = projectId,
                WatchId = watchIdA,
                Source = new ContextSource { EntryId = entryIdA },
                InteractionId = ShortGuid.NewShortGuid(),
                ActionId = HierarchyContextActionProvider.RenameActionId,
            },
            headers,
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
        using var channel = CreateChannel();
        var hierarchyClient = new HierarchyService.HierarchyServiceClient(channel);
        var contextClient = new ContextService.ContextServiceClient(channel);
        var headers = await LoginAsync(channel);
        var projectId = await AddProjectAsync(channel, headers);
        var watchIdA = ShortGuid.NewShortGuid();
        var watchIdB = ShortGuid.NewShortGuid();

        // Arrange, continued.
        var entriesA = await hierarchyClient.ListEntriesAsync(new ListEntriesRequest { ProjectId = projectId, WatchId = watchIdA }, headers, cancellationToken: TestContext.Current.CancellationToken);
        var entryIdA = entriesA.Entries.Entries_.Single(e => e.Name == "shared.txt").Id;

        // Act.
        var discovered = await contextClient.DiscoverActionsAsync(
            new DiscoverActionsRequest
            {
                ProjectId = projectId,
                WatchId = watchIdB,
                Source = new ContextSource { EntryId = entryIdA },
            },
            headers,
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
        using var ownerChannel = CreateChannel();
        var ownerHeaders = await LoginAsync(ownerChannel);
        var ownerClient = new HierarchyService.HierarchyServiceClient(ownerChannel);
        var ownerContextClient = new ContextService.ContextServiceClient(ownerChannel);
        var projectId = await AddProjectAsync(ownerChannel, ownerHeaders);
        var watchId = ShortGuid.NewShortGuid();
        var entries = await ownerClient.ListEntriesAsync(new ListEntriesRequest { ProjectId = projectId, WatchId = watchId }, ownerHeaders, cancellationToken: TestContext.Current.CancellationToken);
        var entryId = entries.Entries.Entries_.Single(e => e.Name == "private.txt").Id;

        // Arrange, continued.
        // A second login is a second session; the project was never added under it, so the
        // project id resolves to nothing for this caller.
        using var otherChannel = CreateChannel();
        var otherHeaders = new Metadata { { SessionTokenHeader, "not-a-valid-session" } };
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
        using var channel = CreateChannel();
        var hierarchyClient = new HierarchyService.HierarchyServiceClient(channel);
        var contextClient = new ContextService.ContextServiceClient(channel);
        var headers = await LoginAsync(channel);
        var projectId = await AddProjectAsync(channel, headers);
        var watchId = ShortGuid.NewShortGuid();

        // Arrange, continued.
        var entries = await hierarchyClient.ListEntriesAsync(new ListEntriesRequest { ProjectId = projectId, WatchId = watchId }, headers, cancellationToken: TestContext.Current.CancellationToken);
        var entryId = entries.Entries.Entries_.Single(e => e.Name == "selected.txt").Id;

        // Arrange, continued.
        using var cts = CreateMessageTimeout();
        using var contextCall = contextClient.Watch(new WatchContextRequest { ProjectId = projectId, WatchId = watchId }, headers, cancellationToken: TestContext.Current.CancellationToken);
        var pendingPrompt = ReadUntilPromptAsync(contextCall.ResponseStream, cts.Token);
        await Task.Delay(StreamStartupGrace, TestContext.Current.CancellationToken);

        // Arrange, continued.
        var nothingSelected = await contextClient.ExecuteActionAsync(
            new ExecuteActionRequest
            {
                ProjectId = projectId,
                WatchId = watchId,
                InteractionId = ShortGuid.NewShortGuid(),
                ActionId = HierarchyContextActionProvider.RenameActionId,
            },
            headers,
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.False(nothingSelected.Accepted);

        // Arrange, continued.
        var selection = new ContextSelection { Source = ContextSelectionSource.Explorer, Id = new ContextSource { EntryId = entryId } };
        var selected = await contextClient.SelectAsync(new SelectRequest { ProjectId = projectId, WatchId = watchId, Selection = selection }, headers, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("", selected.Error);

        // Act.
        var executed = await contextClient.ExecuteActionAsync(
            new ExecuteActionRequest
            {
                ProjectId = projectId,
                WatchId = watchId,
                InteractionId = ShortGuid.NewShortGuid(),
                ActionId = HierarchyContextActionProvider.RenameActionId,
            },
            headers,
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
        using var channel = CreateChannel();
        var hierarchyClient = new HierarchyService.HierarchyServiceClient(channel);
        var contextClient = new ContextService.ContextServiceClient(channel);
        var headers = await LoginAsync(channel);
        var projectId = await AddProjectAsync(channel, headers);
        var watchId = ShortGuid.NewShortGuid();

        // Arrange, continued.
        var entries = await hierarchyClient.ListEntriesAsync(new ListEntriesRequest { ProjectId = projectId, WatchId = watchId }, headers, cancellationToken: TestContext.Current.CancellationToken);
        var entryId = entries.Entries.Entries_.Single(e => e.Name == "once.txt").Id;

        // Arrange, continued.
        using var call = hierarchyClient.WatchHierarchy(new WatchHierarchyRequest { ProjectId = projectId, WatchId = watchId }, headers, cancellationToken: TestContext.Current.CancellationToken);
        using var cts = CreateMessageTimeout();
        using var contextCall = contextClient.Watch(new WatchContextRequest { ProjectId = projectId, WatchId = watchId }, headers, cancellationToken: TestContext.Current.CancellationToken);
        var pendingPrompt = ReadUntilPromptAsync(contextCall.ResponseStream, cts.Token);
        await Task.Delay(StreamStartupGrace, TestContext.Current.CancellationToken);

        // Arrange, continued.
        var interactionId = ShortGuid.NewShortGuid();
        await contextClient.ExecuteActionAsync(
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
        await pendingPrompt;

        // Act.
        var first = await contextClient.SubmitInteractionAsync(
            new SubmitInteractionRequest { InteractionId = interactionId, Value = "twice.txt" }, headers, cancellationToken: TestContext.Current.CancellationToken);
        var second = await contextClient.SubmitInteractionAsync(
            new SubmitInteractionRequest { InteractionId = interactionId, Value = "thrice.txt" }, headers, cancellationToken: TestContext.Current.CancellationToken);

        // Assert.
        Assert.True(first.Completed);
        Assert.False(second.Completed);
        Assert.True(File.Exists(IoPath.Combine(_projectFolder, "twice.txt")));
        Assert.False(File.Exists(IoPath.Combine(_projectFolder, "thrice.txt")));
    }
}
