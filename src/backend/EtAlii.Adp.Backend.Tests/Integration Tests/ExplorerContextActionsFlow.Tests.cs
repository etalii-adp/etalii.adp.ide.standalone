using EtAlii.Adp.Backend.Hierarchy;
using EtAlii.Adp.Backend.Projects;
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
            });
        });
    }

    public void Dispose()
    {
        _factory.Dispose();
        if (Directory.Exists(_appDataRoot))
        {
            Directory.Delete(_appDataRoot, recursive: true);
        }
    }

    private GrpcChannel CreateChannel()
    {
        var httpClient = _factory.CreateDefaultClient();
        return GrpcChannel.ForAddress(httpClient.BaseAddress!, new GrpcChannelOptions { HttpClient = httpClient });
    }

    private static async Task<Metadata> LoginAsync(GrpcChannel channel)
    {
        var authClient = new AuthenticationService.AuthenticationServiceClient(channel);
        var response = await authClient.LoginAsync(new LoginRequest { Username = DeveloperUsername, Credential = DeveloperCredential });
        return new Metadata { { SessionTokenHeader, response.Session.Value } };
    }

    private async Task<ShortGuid> AddProjectAsync(GrpcChannel channel, Metadata headers, string? folder = null)
    {
        var projectClient = new ProjectService.ProjectServiceClient(channel);
        var pathMessage = new Path();
        pathMessage.Segments.AddRange((folder ?? _projectFolder).Split(IoPath.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries));
        var response = await projectClient.AddProjectAsync(new AddProjectRequest { Path = pathMessage }, headers);
        return response.Added.Id;
    }

    /// <summary>Reads until a message of the wanted kind arrives, skipping anything else on the stream.</summary>
    private static async Task<HierarchyMessage> ReadUntilAsync(
        IAsyncStreamReader<HierarchyMessage> stream, HierarchyMessage.MessageOneofCase wanted, CancellationToken cancellationToken)
    {
        while (await stream.MoveNext(cancellationToken))
        {
            if (stream.Current.MessageCase == wanted)
            {
                return stream.Current;
            }
        }

        throw new InvalidOperationException($"The stream ended before a {wanted} message arrived.");
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
        File.WriteAllText(IoPath.Combine(_projectFolder, "original.txt"), "content");
        File.WriteAllText(IoPath.Combine(_projectFolder, "taken.txt"), "");

        using var channel = CreateChannel();
        var hierarchyClient = new HierarchyService.HierarchyServiceClient(channel);
        var headers = await LoginAsync(channel);
        var projectId = await AddProjectAsync(channel, headers);
        var watchId = ShortGuid.NewShortGuid();

        var entries = await hierarchyClient.ListEntriesAsync(new ListEntriesRequest { ProjectId = projectId, WatchId = watchId }, headers);
        var entryId = entries.Entries.Entries_.Single(e => e.Name == "original.txt").Id;

        using var call = hierarchyClient.WatchHierarchy(new WatchHierarchyRequest { ProjectId = projectId, WatchId = watchId }, headers);
        using var cts = new CancellationTokenSource(MessageTimeout);
        var pendingPrompt = ReadUntilAsync(call.ResponseStream, HierarchyMessage.MessageOneofCase.Prompt, cts.Token);
        await Task.Delay(StreamStartupGrace);

        var source = new ContextSource { EntryId = entryId };
        var discovered = await hierarchyClient.DiscoverActionsAsync(
            new DiscoverActionsRequest { ProjectId = projectId, WatchId = watchId, Scope = ContextScope.Hierarchy, Source = source },
            headers);
        var renameAction = discovered.Groups.SelectMany(g => g.Actions).Single(a => a.Id == HierarchyContextActionProvider.RenameActionId);
        Assert.True(renameAction.Available);

        var interactionId = ShortGuid.NewShortGuid();
        var executed = await hierarchyClient.ExecuteActionAsync(
            new ExecuteActionRequest
            {
                ProjectId = projectId,
                WatchId = watchId,
                Scope = ContextScope.Hierarchy,
                Source = source,
                InteractionId = interactionId,
                ActionId = renameAction.Id,
            },
            headers);
        Assert.True(executed.Accepted);

        var prompt = (await pendingPrompt).Prompt;
        Assert.Equal(ContextPrompt.PromptOneofCase.InputDialog, prompt.PromptCase);
        Assert.Equal((ShortGuid)interactionId, (ShortGuid)prompt.InteractionId);
        Assert.Equal("original.txt", prompt.InputDialog.InitialValue);

        var rejected = await hierarchyClient.ProposeInputAsync(
            new ProposeInputRequest { InteractionId = interactionId, Revision = 1, Value = "taken.txt" }, headers);
        Assert.False(rejected.Valid);
        Assert.Equal(1u, rejected.Revision);

        var accepted = await hierarchyClient.ProposeInputAsync(
            new ProposeInputRequest { InteractionId = interactionId, Revision = 2, Value = "renamed.txt" }, headers);
        Assert.True(accepted.Valid);
        Assert.Equal(2u, accepted.Revision);

        var pendingChange = ReadUntilChangeAsync(call.ResponseStream, HierarchyChange.ChangeOneofCase.Renamed, cts.Token);

        var submitted = await hierarchyClient.SubmitInteractionAsync(
            new SubmitInteractionRequest { InteractionId = interactionId, Value = "renamed.txt" }, headers);
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
        var folder = IoPath.Combine(_projectFolder, "doomed");
        Directory.CreateDirectory(IoPath.Combine(folder, "inner"));
        File.WriteAllText(IoPath.Combine(folder, "inner", "leaf.txt"), "");

        using var channel = CreateChannel();
        var hierarchyClient = new HierarchyService.HierarchyServiceClient(channel);
        var headers = await LoginAsync(channel);
        var projectId = await AddProjectAsync(channel, headers);
        var watchId = ShortGuid.NewShortGuid();

        var entries = await hierarchyClient.ListEntriesAsync(new ListEntriesRequest { ProjectId = projectId, WatchId = watchId }, headers);
        var entryId = entries.Entries.Entries_.Single(e => e.Name == "doomed").Id;

        using var call = hierarchyClient.WatchHierarchy(new WatchHierarchyRequest { ProjectId = projectId, WatchId = watchId }, headers);
        using var cts = new CancellationTokenSource(MessageTimeout);
        var pendingPrompt = ReadUntilAsync(call.ResponseStream, HierarchyMessage.MessageOneofCase.Prompt, cts.Token);
        await Task.Delay(StreamStartupGrace);

        var interactionId = ShortGuid.NewShortGuid();
        var executed = await hierarchyClient.ExecuteActionAsync(
            new ExecuteActionRequest
            {
                ProjectId = projectId,
                WatchId = watchId,
                Scope = ContextScope.Hierarchy,
                Source = new ContextSource { EntryId = entryId },
                InteractionId = interactionId,
                ActionId = HierarchyContextActionProvider.DeleteActionId,
            },
            headers);
        Assert.True(executed.Accepted);

        var prompt = (await pendingPrompt).Prompt;
        Assert.Equal(ContextPrompt.PromptOneofCase.ConfirmDialog, prompt.PromptCase);
        Assert.True(prompt.ConfirmDialog.Danger);
        Assert.Contains("doomed", prompt.ConfirmDialog.Message);

        var pendingChange = ReadUntilChangeAsync(call.ResponseStream, HierarchyChange.ChangeOneofCase.Removed, cts.Token);

        var submitted = await hierarchyClient.SubmitInteractionAsync(
            new SubmitInteractionRequest { InteractionId = interactionId, Value = "" }, headers);
        Assert.True(submitted.Completed);
        Assert.False(Directory.Exists(folder));

        var change = await pendingChange;
        Assert.Equal((ShortGuid)entryId, (ShortGuid)change.Removed.EntryId);
    }

    [Fact]
    public async Task ExecuteAction_TriggeredByShortcutWithNoPriorDiscovery_ProducesTheSamePromptAsTheActionIdPath()
    {
        File.WriteAllText(IoPath.Combine(_projectFolder, "shortcut.txt"), "");

        using var channel = CreateChannel();
        var hierarchyClient = new HierarchyService.HierarchyServiceClient(channel);
        var headers = await LoginAsync(channel);
        var projectId = await AddProjectAsync(channel, headers);
        var watchId = ShortGuid.NewShortGuid();

        var entries = await hierarchyClient.ListEntriesAsync(new ListEntriesRequest { ProjectId = projectId, WatchId = watchId }, headers);
        var entryId = entries.Entries.Entries_.Single(e => e.Name == "shortcut.txt").Id;

        using var call = hierarchyClient.WatchHierarchy(new WatchHierarchyRequest { ProjectId = projectId, WatchId = watchId }, headers);
        using var cts = new CancellationTokenSource(MessageTimeout);
        var pendingPrompt = ReadUntilAsync(call.ResponseStream, HierarchyMessage.MessageOneofCase.Prompt, cts.Token);
        await Task.Delay(StreamStartupGrace);

        var executed = await hierarchyClient.ExecuteActionAsync(
            new ExecuteActionRequest
            {
                ProjectId = projectId,
                WatchId = watchId,
                Scope = ContextScope.Hierarchy,
                Source = new ContextSource { EntryId = entryId },
                InteractionId = ShortGuid.NewShortGuid(),
                Shortcut = new ContextShortcut { Key = "F2" },
            },
            headers);

        Assert.True(executed.Accepted);
        var prompt = (await pendingPrompt).Prompt;
        Assert.Equal(ContextPrompt.PromptOneofCase.InputDialog, prompt.PromptCase);
        Assert.Equal("shortcut.txt", prompt.InputDialog.InitialValue);
    }

    [Fact]
    public async Task APromptRaisedOnOneConnection_IsNeverObservedOnAnothersStream()
    {
        File.WriteAllText(IoPath.Combine(_projectFolder, "shared.txt"), "");

        using var channel = CreateChannel();
        var hierarchyClient = new HierarchyService.HierarchyServiceClient(channel);
        var headers = await LoginAsync(channel);
        var projectId = await AddProjectAsync(channel, headers);
        var watchIdA = ShortGuid.NewShortGuid();
        var watchIdB = ShortGuid.NewShortGuid();

        var entriesA = await hierarchyClient.ListEntriesAsync(new ListEntriesRequest { ProjectId = projectId, WatchId = watchIdA }, headers);
        await hierarchyClient.ListEntriesAsync(new ListEntriesRequest { ProjectId = projectId, WatchId = watchIdB }, headers);
        var entryIdA = entriesA.Entries.Entries_.Single(e => e.Name == "shared.txt").Id;

        using var callA = hierarchyClient.WatchHierarchy(new WatchHierarchyRequest { ProjectId = projectId, WatchId = watchIdA }, headers);
        using var callB = hierarchyClient.WatchHierarchy(new WatchHierarchyRequest { ProjectId = projectId, WatchId = watchIdB }, headers);
        using var ctsA = new CancellationTokenSource(MessageTimeout);
        using var ctsB = new CancellationTokenSource(MessageTimeout);

        var pendingA = callA.ResponseStream.MoveNext(ctsA.Token);
        var pendingB = callB.ResponseStream.MoveNext(ctsB.Token);
        await Task.Delay(StreamStartupGrace);

        await hierarchyClient.ExecuteActionAsync(
            new ExecuteActionRequest
            {
                ProjectId = projectId,
                WatchId = watchIdA,
                Scope = ContextScope.Hierarchy,
                Source = new ContextSource { EntryId = entryIdA },
                InteractionId = ShortGuid.NewShortGuid(),
                ActionId = HierarchyContextActionProvider.RenameActionId,
            },
            headers);

        Assert.True(await pendingA, "Expected the prompt on connection A but its stream ended or timed out.");
        Assert.Equal(HierarchyMessage.MessageOneofCase.Prompt, callA.ResponseStream.Current.MessageCase);

        // Nothing happened on disk, so B has nothing to receive at all - and certainly not
        // A's prompt. Anything arriving within this window is a leak.
        var arrivedOnB = await Task.WhenAny(pendingB, Task.Delay(TimeSpan.FromSeconds(1), CancellationToken.None)) == pendingB;
        Assert.False(arrivedOnB, "Connection B observed a message raised for connection A.");
    }

    [Fact]
    public async Task DiscoverActions_ForAnEntryIdBelongingToAnotherConnection_ReportsNothing()
    {
        File.WriteAllText(IoPath.Combine(_projectFolder, "shared.txt"), "");

        using var channel = CreateChannel();
        var hierarchyClient = new HierarchyService.HierarchyServiceClient(channel);
        var headers = await LoginAsync(channel);
        var projectId = await AddProjectAsync(channel, headers);
        var watchIdA = ShortGuid.NewShortGuid();
        var watchIdB = ShortGuid.NewShortGuid();

        var entriesA = await hierarchyClient.ListEntriesAsync(new ListEntriesRequest { ProjectId = projectId, WatchId = watchIdA }, headers);
        var entryIdA = entriesA.Entries.Entries_.Single(e => e.Name == "shared.txt").Id;

        var discovered = await hierarchyClient.DiscoverActionsAsync(
            new DiscoverActionsRequest
            {
                ProjectId = projectId,
                WatchId = watchIdB,
                Scope = ContextScope.Hierarchy,
                Source = new ContextSource { EntryId = entryIdA },
            },
            headers);

        Assert.Empty(discovered.Groups);
    }

    [Fact]
    public async Task DiscoverActions_ForAProjectTheCallerIsNotAuthorizedFor_ReportsNothing()
    {
        File.WriteAllText(IoPath.Combine(_projectFolder, "private.txt"), "");

        using var ownerChannel = CreateChannel();
        var ownerHeaders = await LoginAsync(ownerChannel);
        var ownerClient = new HierarchyService.HierarchyServiceClient(ownerChannel);
        var projectId = await AddProjectAsync(ownerChannel, ownerHeaders);
        var watchId = ShortGuid.NewShortGuid();
        var entries = await ownerClient.ListEntriesAsync(new ListEntriesRequest { ProjectId = projectId, WatchId = watchId }, ownerHeaders);
        var entryId = entries.Entries.Entries_.Single(e => e.Name == "private.txt").Id;

        // A second login is a second session; the project was never added under it, so the
        // project id resolves to nothing for this caller.
        using var otherChannel = CreateChannel();
        var otherHeaders = new Metadata { { SessionTokenHeader, "not-a-valid-session" } };
        var otherClient = new HierarchyService.HierarchyServiceClient(otherChannel);

        var rpcException = await Assert.ThrowsAsync<RpcException>(async () => await otherClient.DiscoverActionsAsync(
            new DiscoverActionsRequest
            {
                ProjectId = projectId,
                WatchId = watchId,
                Scope = ContextScope.Hierarchy,
                Source = new ContextSource { EntryId = entryId },
            },
            otherHeaders));

        Assert.Equal(StatusCode.Unauthenticated, rpcException.StatusCode);
    }

    [Fact]
    public async Task SubmitInteraction_RepeatedForTheSameId_DoesNothingTheSecondTime()
    {
        File.WriteAllText(IoPath.Combine(_projectFolder, "once.txt"), "");

        using var channel = CreateChannel();
        var hierarchyClient = new HierarchyService.HierarchyServiceClient(channel);
        var headers = await LoginAsync(channel);
        var projectId = await AddProjectAsync(channel, headers);
        var watchId = ShortGuid.NewShortGuid();

        var entries = await hierarchyClient.ListEntriesAsync(new ListEntriesRequest { ProjectId = projectId, WatchId = watchId }, headers);
        var entryId = entries.Entries.Entries_.Single(e => e.Name == "once.txt").Id;

        using var call = hierarchyClient.WatchHierarchy(new WatchHierarchyRequest { ProjectId = projectId, WatchId = watchId }, headers);
        using var cts = new CancellationTokenSource(MessageTimeout);
        var pendingPrompt = ReadUntilAsync(call.ResponseStream, HierarchyMessage.MessageOneofCase.Prompt, cts.Token);
        await Task.Delay(StreamStartupGrace);

        var interactionId = ShortGuid.NewShortGuid();
        await hierarchyClient.ExecuteActionAsync(
            new ExecuteActionRequest
            {
                ProjectId = projectId,
                WatchId = watchId,
                Scope = ContextScope.Hierarchy,
                Source = new ContextSource { EntryId = entryId },
                InteractionId = interactionId,
                ActionId = HierarchyContextActionProvider.RenameActionId,
            },
            headers);
        await pendingPrompt;

        var first = await hierarchyClient.SubmitInteractionAsync(
            new SubmitInteractionRequest { InteractionId = interactionId, Value = "twice.txt" }, headers);
        var second = await hierarchyClient.SubmitInteractionAsync(
            new SubmitInteractionRequest { InteractionId = interactionId, Value = "thrice.txt" }, headers);

        Assert.True(first.Completed);
        Assert.False(second.Completed);
        Assert.True(File.Exists(IoPath.Combine(_projectFolder, "twice.txt")));
        Assert.False(File.Exists(IoPath.Combine(_projectFolder, "thrice.txt")));
    }
}
