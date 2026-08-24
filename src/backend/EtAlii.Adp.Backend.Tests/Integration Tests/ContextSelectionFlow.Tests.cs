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
/// Exercises selection end to end against the real host: the baseline, a pushed
/// selection with its filled-in path, detail and actions, previews, on-disk renames and
/// deletes flowing back as selection changes, and - the property most worth having -
/// one connection never observing another's selection.
/// </summary>
public class ContextSelectionFlowTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private const string DeveloperUsername = "admin";
    private const string DeveloperCredential = "changeme";
    private const string SessionTokenHeader = "session-token";
    private static readonly TimeSpan MessageTimeout = TimeSpan.FromSeconds(10);

    // The hierarchy watcher only runs while a WatchHierarchy stream is open, and Grpc.Net
    // only starts a server stream on its first read - so disk-change tests open that
    // stream, start reading it, and give the watcher this long to come up.
    private static readonly TimeSpan StreamStartupGrace = TimeSpan.FromMilliseconds(500);

    private readonly WebApplicationFactory<Program> _factory;
    private readonly string _appDataRoot;
    private readonly string _projectFolder;

    public ContextSelectionFlowTests(WebApplicationFactory<Program> baseFactory)
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
        if (Directory.Exists(_appDataRoot))
        {
            Directory.Delete(_appDataRoot, recursive: true);
        }
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

    private async Task<ShortGuid> AddProjectAsync(GrpcChannel channel, Metadata headers)
    {
        var projectClient = new ProjectService.ProjectServiceClient(channel);
        var pathMessage = new Path();
        pathMessage.Segments.AddRange(_projectFolder.Split(IoPath.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries));
        var response = await projectClient.AddProjectAsync(new AddProjectRequest { Path = pathMessage }, headers, cancellationToken: TestContext.Current.CancellationToken);
        return response.Added.Id;
    }

    private sealed record Session(
        GrpcChannel Channel,
        Metadata Headers,
        Contracts.ShortGuid ProjectId,
        Contracts.ShortGuid WatchId,
        HierarchyService.HierarchyServiceClient Hierarchy,
        ContextService.ContextServiceClient Context) : IDisposable
    {
        public void Dispose() => Channel.Dispose();
    }

    private async Task<Session> OpenSessionAsync()
    {
        var channel = CreateChannel();
        var headers = await LoginAsync(channel);
        var projectId = await AddProjectAsync(channel, headers);
        return new Session(
            channel, headers, projectId, ShortGuid.NewShortGuid(),
            new HierarchyService.HierarchyServiceClient(channel),
            new ContextService.ContextServiceClient(channel));
    }

    private static async Task<Contracts.ShortGuid> EntryIdOfAsync(Session session, string name)
    {
        var entries = await session.Hierarchy.ListEntriesAsync(
            new ListEntriesRequest { ProjectId = session.ProjectId, WatchId = session.WatchId }, session.Headers, cancellationToken: TestContext.Current.CancellationToken);
        return entries.Entries.Entries_.Single(e => e.Name == name).Id;
    }

    private static ContextSelection Selection(Contracts.ShortGuid entryId, params string[] path)
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
            if (stream.Current.MessageCase == ContextMessage.MessageOneofCase.Problems)
            {
                return;
            }
        }

        throw new InvalidOperationException("The stream ended before the problems baseline arrived.");
    }

    [Fact]
    public async Task Watch_OpensWithAnEmptyBaseline()
    {
        using var session = await OpenSessionAsync();
        using var cts = CreateMessageTimeout();

        using var call = session.Context.Watch(new WatchContextRequest { ProjectId = session.ProjectId, WatchId = session.WatchId }, session.Headers, cancellationToken: TestContext.Current.CancellationToken);
        var baseline = await ReadSelectionAsync(call.ResponseStream, cts.Token);

        Assert.Null(baseline.Selection);
        Assert.Empty(baseline.Levels);
        Assert.False(baseline.Transient);
    }

    [Fact]
    public async Task Select_AFile_PushesTheChainWithFilledPathDetailAndActions()
    {
        Directory.CreateDirectory(IoPath.Combine(_projectFolder, "docs"));
        File.WriteAllText(IoPath.Combine(_projectFolder, "docs", "design.mm"), "");
        using var session = await OpenSessionAsync();
        using var cts = CreateMessageTimeout();
        var docsId = await EntryIdOfAsync(session, "docs");
        var listed = await session.Hierarchy.ListEntriesAsync(
            new ListEntriesRequest { ProjectId = session.ProjectId, WatchId = session.WatchId, FolderId = docsId }, session.Headers, cancellationToken: TestContext.Current.CancellationToken);
        var fileId = listed.Entries.Entries_.Single().Id;

        using var call = session.Context.Watch(new WatchContextRequest { ProjectId = session.ProjectId, WatchId = session.WatchId }, session.Headers, cancellationToken: TestContext.Current.CancellationToken);
        await ReadSelectionAsync(call.ResponseStream, cts.Token);

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
        var actionIds = pushed.Actions.SelectMany(g => g.Actions).Select(a => a.Id).ToList();
        Assert.Contains(HierarchyContextActionProvider.RenameActionId, actionIds);
        Assert.Contains(HierarchyContextActionProvider.DeleteActionId, actionIds);
    }

    [Fact]
    public async Task Select_PushesTheDiagramTypeForARegistration_AndNoTypeForAPlainFile()
    {
        // The whole tab system hangs off this one field arriving on the pushed detail
        // (diagram-workspace-tabs Requirement 1), so it is proven over the real resolver,
        // router and stream rather than against mocks.
        File.WriteAllText(IoPath.Combine(_projectFolder, "domain.adp"), "freeplane/mindmap\n");
        File.WriteAllText(IoPath.Combine(_projectFolder, "domain.mm"), "<map version=\"freeplane 1.11.5\">\n<node TEXT=\"domain\" ID=\"ID_1\"/>\n</map>\n");
        File.WriteAllText(IoPath.Combine(_projectFolder, "readme.txt"), "");
        using var session = await OpenSessionAsync();
        using var cts = CreateMessageTimeout();
        var adpId = await EntryIdOfAsync(session, "domain.adp");
        var plainId = await EntryIdOfAsync(session, "readme.txt");

        using var call = session.Context.Watch(new WatchContextRequest { ProjectId = session.ProjectId, WatchId = session.WatchId }, session.Headers, cancellationToken: TestContext.Current.CancellationToken);
        await ReadSelectionAsync(call.ResponseStream, cts.Token);

        await session.Context.SelectAsync(
            new SelectRequest { ProjectId = session.ProjectId, WatchId = session.WatchId, Selection = Selection(adpId) }, session.Headers, cancellationToken: TestContext.Current.CancellationToken);
        var diagram = await ReadSelectionAsync(call.ResponseStream, cts.Token);
        Assert.Equal("freeplane/mindmap", Assert.Single(diagram.Levels).Entry.DiagramMimeType);

        await session.Context.SelectAsync(
            new SelectRequest { ProjectId = session.ProjectId, WatchId = session.WatchId, Selection = Selection(plainId) }, session.Headers, cancellationToken: TestContext.Current.CancellationToken);
        var plain = await ReadSelectionAsync(call.ResponseStream, cts.Token);
        Assert.Equal("", Assert.Single(plain.Levels).Entry.DiagramMimeType);
    }

    [Fact]
    public async Task Select_WithAMismatchingPath_IsRejectedAndPushesNothing()
    {
        File.WriteAllText(IoPath.Combine(_projectFolder, "a.txt"), "");
        using var session = await OpenSessionAsync();
        using var cts = CreateMessageTimeout();
        var entryId = await EntryIdOfAsync(session, "a.txt");
        using var call = session.Context.Watch(new WatchContextRequest { ProjectId = session.ProjectId, WatchId = session.WatchId }, session.Headers, cancellationToken: TestContext.Current.CancellationToken);
        await TestBaselineAsync(call.ResponseStream, cts.Token);

        var response = await session.Context.SelectAsync(
            new SelectRequest { ProjectId = session.ProjectId, WatchId = session.WatchId, Selection = Selection(entryId, "b.txt") }, session.Headers, cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotEqual("", response.Error);
        var pending = call.ResponseStream.MoveNext(cts.Token);
        var arrived = await Task.WhenAny(pending, Task.Delay(TimeSpan.FromMilliseconds(500), TestContext.Current.CancellationToken)) == pending;
        Assert.False(arrived, "A rejected Select must not push anything.");
    }

    [Fact]
    public async Task Select_Preview_PushesTransientAndLeavesTheCurrentSelectionAlone()
    {
        File.WriteAllText(IoPath.Combine(_projectFolder, "current.txt"), "");
        File.WriteAllText(IoPath.Combine(_projectFolder, "preview.txt"), "");
        using var session = await OpenSessionAsync();
        using var cts = CreateMessageTimeout();
        var currentId = await EntryIdOfAsync(session, "current.txt");
        var previewId = await EntryIdOfAsync(session, "preview.txt");
        using var call = session.Context.Watch(new WatchContextRequest { ProjectId = session.ProjectId, WatchId = session.WatchId }, session.Headers, cancellationToken: TestContext.Current.CancellationToken);
        await ReadSelectionAsync(call.ResponseStream, cts.Token);
        await session.Context.SelectAsync(
            new SelectRequest { ProjectId = session.ProjectId, WatchId = session.WatchId, Selection = Selection(currentId) }, session.Headers, cancellationToken: TestContext.Current.CancellationToken);
        await ReadSelectionAsync(call.ResponseStream, cts.Token);

        var preview = Selection(previewId);
        preview.Action = ContextSelectionAction.Preview;
        await session.Context.SelectAsync(
            new SelectRequest { ProjectId = session.ProjectId, WatchId = session.WatchId, Selection = preview }, session.Headers, cancellationToken: TestContext.Current.CancellationToken);
        var transient = await ReadSelectionAsync(call.ResponseStream, cts.Token);
        Assert.True(transient.Transient);
        Assert.Equal(new[] { "preview.txt" }, transient.Selection.Path.Segments);

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

        Assert.Fail("No prompt arrived for the action on the current selection.");
    }

    [Fact]
    public async Task RenameOnDisk_PushesTheSelectionWithItsNewPathAndTheSameId()
    {
        var original = IoPath.Combine(_projectFolder, "original.txt");
        File.WriteAllText(original, "");
        using var session = await OpenSessionAsync();
        using var cts = CreateMessageTimeout();
        var entryId = await EntryIdOfAsync(session, "original.txt");

        using var hierarchyCall = session.Hierarchy.WatchHierarchy(new WatchHierarchyRequest { ProjectId = session.ProjectId, WatchId = session.WatchId }, session.Headers, cancellationToken: TestContext.Current.CancellationToken);
        _ = hierarchyCall.ResponseStream.MoveNext(cts.Token);
        using var call = session.Context.Watch(new WatchContextRequest { ProjectId = session.ProjectId, WatchId = session.WatchId }, session.Headers, cancellationToken: TestContext.Current.CancellationToken);
        await ReadSelectionAsync(call.ResponseStream, cts.Token);
        await session.Context.SelectAsync(
            new SelectRequest { ProjectId = session.ProjectId, WatchId = session.WatchId, Selection = Selection(entryId) }, session.Headers, cancellationToken: TestContext.Current.CancellationToken);
        await ReadSelectionAsync(call.ResponseStream, cts.Token);
        await Task.Delay(StreamStartupGrace, TestContext.Current.CancellationToken);

        File.Move(original, IoPath.Combine(_projectFolder, "renamed.txt"));

        var pushed = await ReadSelectionAsync(call.ResponseStream, cts.Token);
        Assert.Equal(new[] { "renamed.txt" }, pushed.Selection.Path.Segments);
        Assert.Equal(entryId, pushed.Selection.Id.EntryId);
    }

    [Fact]
    public async Task DeleteOnDisk_PushesAnEmptySelection()
    {
        var file = IoPath.Combine(_projectFolder, "doomed.txt");
        File.WriteAllText(file, "");
        using var session = await OpenSessionAsync();
        using var cts = CreateMessageTimeout();
        var entryId = await EntryIdOfAsync(session, "doomed.txt");

        using var hierarchyCall = session.Hierarchy.WatchHierarchy(new WatchHierarchyRequest { ProjectId = session.ProjectId, WatchId = session.WatchId }, session.Headers, cancellationToken: TestContext.Current.CancellationToken);
        _ = hierarchyCall.ResponseStream.MoveNext(cts.Token);
        using var call = session.Context.Watch(new WatchContextRequest { ProjectId = session.ProjectId, WatchId = session.WatchId }, session.Headers, cancellationToken: TestContext.Current.CancellationToken);
        await ReadSelectionAsync(call.ResponseStream, cts.Token);
        await session.Context.SelectAsync(
            new SelectRequest { ProjectId = session.ProjectId, WatchId = session.WatchId, Selection = Selection(entryId) }, session.Headers, cancellationToken: TestContext.Current.CancellationToken);
        await ReadSelectionAsync(call.ResponseStream, cts.Token);
        await Task.Delay(StreamStartupGrace, TestContext.Current.CancellationToken);

        File.Delete(file);

        var pushed = await ReadSelectionAsync(call.ResponseStream, cts.Token);
        Assert.Null(pushed.Selection);
    }

    [Fact]
    public async Task Select_WithAnIdFromAnotherConnection_IsRejected()
    {
        File.WriteAllText(IoPath.Combine(_projectFolder, "shared.txt"), "");
        using var session = await OpenSessionAsync();
        var foreignWatchId = ShortGuid.NewShortGuid();
        var foreignEntries = await session.Hierarchy.ListEntriesAsync(
            new ListEntriesRequest { ProjectId = session.ProjectId, WatchId = foreignWatchId }, session.Headers, cancellationToken: TestContext.Current.CancellationToken);
        var foreignId = foreignEntries.Entries.Entries_.Single().Id;

        var response = await session.Context.SelectAsync(
            new SelectRequest { ProjectId = session.ProjectId, WatchId = session.WatchId, Selection = Selection(foreignId) }, session.Headers, cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotEqual("", response.Error);
    }

    [Fact]
    public async Task ASelectionOnOneConnection_IsNeverObservedOnAnothersStream()
    {
        File.WriteAllText(IoPath.Combine(_projectFolder, "shared.txt"), "");
        using var session = await OpenSessionAsync();
        using var ctsA = CreateMessageTimeout();
        using var ctsB = CreateMessageTimeout();
        var watchIdB = ShortGuid.NewShortGuid();
        var entryIdA = await EntryIdOfAsync(session, "shared.txt");

        using var callA = session.Context.Watch(new WatchContextRequest { ProjectId = session.ProjectId, WatchId = session.WatchId }, session.Headers, cancellationToken: TestContext.Current.CancellationToken);
        using var callB = session.Context.Watch(new WatchContextRequest { ProjectId = session.ProjectId, WatchId = watchIdB }, session.Headers, cancellationToken: TestContext.Current.CancellationToken);
        await TestBaselineAsync(callA.ResponseStream, ctsA.Token);
        await TestBaselineAsync(callB.ResponseStream, ctsB.Token);
        var pendingB = callB.ResponseStream.MoveNext(ctsB.Token);

        await session.Context.SelectAsync(
            new SelectRequest { ProjectId = session.ProjectId, WatchId = session.WatchId, Selection = Selection(entryIdA) }, session.Headers, cancellationToken: TestContext.Current.CancellationToken);

        var pushedA = await ReadSelectionAsync(callA.ResponseStream, ctsA.Token);
        Assert.Equal(new[] { "shared.txt" }, pushedA.Selection.Path.Segments);
        var arrivedOnB = await Task.WhenAny(pendingB, Task.Delay(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken)) == pendingB;
        Assert.False(arrivedOnB, "Connection B observed a selection made on connection A.");
    }

    [Fact]
    public async Task SelectAndWatch_ForAProjectTheCallerIsNotAuthorizedFor_AreRejected()
    {
        using var owner = await OpenSessionAsync();
        using var otherChannel = CreateChannel();
        var otherHeaders = new Metadata { { SessionTokenHeader, "not-a-valid-session" } };
        var otherClient = new ContextService.ContextServiceClient(otherChannel);

        var selectFailure = await Assert.ThrowsAsync<RpcException>(async () => await otherClient.SelectAsync(
            new SelectRequest { ProjectId = owner.ProjectId, WatchId = owner.WatchId }, otherHeaders, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(StatusCode.Unauthenticated, selectFailure.StatusCode);

        using var cts = CreateMessageTimeout();
        using var call = otherClient.Watch(new WatchContextRequest { ProjectId = owner.ProjectId, WatchId = owner.WatchId }, otherHeaders, cancellationToken: TestContext.Current.CancellationToken);
        var watchFailure = await Assert.ThrowsAsync<RpcException>(async () => await call.ResponseStream.MoveNext(cts.Token));
        Assert.Equal(StatusCode.Unauthenticated, watchFailure.StatusCode);
    }
}
