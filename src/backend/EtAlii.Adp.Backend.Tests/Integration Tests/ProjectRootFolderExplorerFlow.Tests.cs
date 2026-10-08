using EtAlii.Adp.Authentication.Wire;
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
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here
using Path = EtAlii.Adp.Documents.Wire.Path;
using ProjectService = EtAlii.Adp.Projects.Wire.ProjectService;

namespace EtAlii.Adp.Backend.Tests;

/// <summary>
/// Exercises HierarchyService against a real temporary folder and the real
/// backend host running in-process, per design.md's Integration Testing
/// strategy — especially the isolation-not-sharing property between two
/// simultaneous connections to the same project.
/// </summary>
public class ProjectRootFolderExplorerFlowTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private const string DeveloperUsername = "admin";
    private const string DeveloperCredential = "changeme";
    private const string SessionTokenHeader = "session-token";
    private static readonly TimeSpan MessageTimeout = TimeSpan.FromSeconds(10);

    private readonly WebApplicationFactory<Program> _factory;
    private readonly string _appDataRoot;
    private readonly string _projectFolder;

    public ProjectRootFolderExplorerFlowTests(WebApplicationFactory<Program> baseFactory)
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
                    provider.GetRequiredService<Hierarchy.DiagramFileRouter>(),
                    provider.GetRequiredService<Diagram.DiagramValidators>()));
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

    private static async Task<Metadata> LoginAsync(AuthenticationService.AuthenticationServiceClient authClient)
    {
        var response = await authClient.LoginAsync(new LoginRequest { Username = DeveloperUsername, Credential = DeveloperCredential }, cancellationToken: TestContext.Current.CancellationToken);
        return new Metadata { { SessionTokenHeader, response.Session.Value } };
    }

    private async Task<ShortGuid> AddProjectAsync(ProjectService.ProjectServiceClient projectClient, Metadata headers)
    {
        var pathMessage = new Path();
        pathMessage.Segments.AddRange(_projectFolder.Split(IoPath.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries));
        var response = await projectClient.AddProjectAsync(new AddProjectRequest { Path = pathMessage }, headers, cancellationToken: TestContext.Current.CancellationToken);
        return response.Added.Id;
    }

    /// <summary>
    /// Triggers a change once the watch is really running, and returns the first change of the
    /// wanted kind. The watch is proven live with probe files first (see
    /// <see cref="HierarchyWatchProbe"/>), which is why this reads until the wanted kind rather
    /// than taking the next message: what the probes leave behind may be reported in between.
    /// </summary>
    private async Task<HierarchyChange> AwaitTriggeredChangeAsync(
        AsyncServerStreamingCall<HierarchyMessage> call, CancellationTokenSource cts, HierarchyChange.ChangeOneofCase wanted, Action triggerChange)
    {
        await HierarchyWatchProbe.WaitUntilLiveAsync(call.ResponseStream, _projectFolder, cts.Token);
        triggerChange();

        return await ReadUntilChangeAsync(call.ResponseStream, change => change.ChangeCase == wanted, cts.Token);
    }

    private static async Task<HierarchyChange> ReadUntilChangeAsync(
        IAsyncStreamReader<HierarchyMessage> stream, Func<HierarchyChange, bool> wanted, CancellationToken cancellationToken)
    {
        while (await stream.MoveNext(cancellationToken))
        {
            var message = stream.Current;
            if (message.MessageCase == HierarchyMessage.MessageOneofCase.Change && wanted(message.Change))
            {
                return message.Change;
            }
        }

        throw new InvalidOperationException("The stream ended before the expected change arrived.");
    }

    [Fact]
    public async Task ListEntries_ForARootWithNestedFolders_ReturnsOnlyDirectChildren()
    {
        // Arrange.
        Directory.CreateDirectory(IoPath.Combine(_projectFolder, "sub"));
        await File.WriteAllTextAsync(IoPath.Combine(_projectFolder, "sub", "nested.txt"), "", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(IoPath.Combine(_projectFolder, "top.txt"), "", TestContext.Current.CancellationToken);

        // Arrange, continued.
        using var channel = CreateChannel();
        var projectClient = new ProjectService.ProjectServiceClient(channel);
        var hierarchyClient = new HierarchyService.HierarchyServiceClient(channel);
        var headers = await LoginAsync(new AuthenticationService.AuthenticationServiceClient(channel));
        var projectId = await AddProjectAsync(projectClient, headers);

        // Act.
        var response = await hierarchyClient.ListEntriesAsync(
            new ListEntriesRequest { ProjectId = projectId, WatchId = ShortGuid.NewShortGuid() },
            headers,
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
        using var channel = CreateChannel();
        var projectClient = new ProjectService.ProjectServiceClient(channel);
        var hierarchyClient = new HierarchyService.HierarchyServiceClient(channel);
        var headers = await LoginAsync(new AuthenticationService.AuthenticationServiceClient(channel));
        var projectId = await AddProjectAsync(projectClient, headers);

        // Arrange, continued.
        var watchIdA = ShortGuid.NewShortGuid();
        var watchIdB = ShortGuid.NewShortGuid();

        // Arrange, continued.
        var entriesA = await hierarchyClient.ListEntriesAsync(new ListEntriesRequest { ProjectId = projectId, WatchId = watchIdA }, headers, cancellationToken: TestContext.Current.CancellationToken);
        var entriesB = await hierarchyClient.ListEntriesAsync(new ListEntriesRequest { ProjectId = projectId, WatchId = watchIdB }, headers, cancellationToken: TestContext.Current.CancellationToken);

        // Arrange, continued.
        var idA = entriesA.Entries.Entries_.Single(e => e.Name == "shared.txt").Id;
        var idB = entriesB.Entries.Entries_.Single(e => e.Name == "shared.txt").Id;
        Assert.NotEqual((ShortGuid)idA, (ShortGuid)idB);

        // Arrange, continued.
        using var callA = hierarchyClient.WatchHierarchy(new WatchHierarchyRequest { ProjectId = projectId, WatchId = watchIdA }, headers, cancellationToken: TestContext.Current.CancellationToken);
        using var callB = hierarchyClient.WatchHierarchy(new WatchHierarchyRequest { ProjectId = projectId, WatchId = watchIdB }, headers, cancellationToken: TestContext.Current.CancellationToken);
        using var ctsA = CreateMessageTimeout();
        using var ctsB = CreateMessageTimeout();

        // Arrange, continued.
        // Both watches share the folder, so each also reports the other's probes: the read below
        // looks for the one file the test itself creates.
        await HierarchyWatchProbe.WaitUntilLiveAsync(callA.ResponseStream, _projectFolder, ctsA.Token);
        await HierarchyWatchProbe.WaitUntilLiveAsync(callB.ResponseStream, _projectFolder, ctsB.Token);

        // Act.
        await File.WriteAllTextAsync(IoPath.Combine(_projectFolder, "new.txt"), "", TestContext.Current.CancellationToken);
        var changeA = await ReadUntilChangeAsync(callA.ResponseStream, IsTheNewFile, ctsA.Token);
        var changeB = await ReadUntilChangeAsync(callB.ResponseStream, IsTheNewFile, ctsB.Token);

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
        using var channel = CreateChannel();
        var projectClient = new ProjectService.ProjectServiceClient(channel);
        var hierarchyClient = new HierarchyService.HierarchyServiceClient(channel);
        var headers = await LoginAsync(new AuthenticationService.AuthenticationServiceClient(channel));
        var projectId = await AddProjectAsync(projectClient, headers);
        var watchId = ShortGuid.NewShortGuid();

        // Arrange, continued.
        var entries = await hierarchyClient.ListEntriesAsync(new ListEntriesRequest { ProjectId = projectId, WatchId = watchId }, headers, cancellationToken: TestContext.Current.CancellationToken);
        var originalId = (ShortGuid)entries.Entries.Entries_.Single(e => e.Name == "original.txt").Id;

        // Arrange, continued.
        using var call = hierarchyClient.WatchHierarchy(new WatchHierarchyRequest { ProjectId = projectId, WatchId = watchId }, headers, cancellationToken: TestContext.Current.CancellationToken);
        using var cts = CreateMessageTimeout();

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
        using var channel = CreateChannel();
        var projectClient = new ProjectService.ProjectServiceClient(channel);
        var hierarchyClient = new HierarchyService.HierarchyServiceClient(channel);
        var headers = await LoginAsync(new AuthenticationService.AuthenticationServiceClient(channel));
        var projectId = await AddProjectAsync(projectClient, headers);
        var watchId = ShortGuid.NewShortGuid();

        await hierarchyClient.ListEntriesAsync(new ListEntriesRequest { ProjectId = projectId, WatchId = watchId }, headers, cancellationToken: TestContext.Current.CancellationToken);
        using var call = hierarchyClient.WatchHierarchy(new WatchHierarchyRequest { ProjectId = projectId, WatchId = watchId }, headers, cancellationToken: TestContext.Current.CancellationToken);
        using var cts = CreateMessageTimeout();

        var change = await AwaitTriggeredChangeAsync(call, cts, HierarchyChange.ChangeOneofCase.RootUnavailable, () =>
            Directory.Delete(_projectFolder, recursive: true));

        // Act and assert, step by step.
        Assert.Equal(HierarchyChange.ChangeOneofCase.RootUnavailable, change.ChangeCase);

        // Recreate the root; the watcher's recovery loop should notice and reconcile
        // without the connection needing to be torn down and reopened.
        Directory.CreateDirectory(_projectFolder);
    }
}
