using EtAlii.Adp.Authentication.Wire;
using EtAlii.Adp.Diagram;
using EtAlii.Adp.Diagram.Wire;
using EtAlii.Adp.Documents.Wire;
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
using IoPath = System.IO.Path;
using Path = EtAlii.Adp.Documents.Wire.Path;
using ProjectService = EtAlii.Adp.Projects.Wire.ProjectService;
using WorkspaceService = EtAlii.Adp.Diagram.Wire.WorkspaceService;

namespace EtAlii.Adp.Backend.Tests;

/// <summary>
/// A tab's one stream against the real host (two-tab-connection-wedge Requirement 3.1): the
/// context, the hierarchy and a diagram's deltas all arriving on <c>WorkspaceService.Watch</c>, a
/// diagram joining and leaving it through the unary calls, and everything it carried ending with it.
/// </summary>
/// <remarks>
/// The sources themselves - the context stream, the hierarchy watch and the diagram pump - are the
/// same code their own RPCs run, and the flow tests beside this one exercise them through those
/// RPCs. What is new, and what this covers, is the carrying: which member a message arrives as,
/// that a diagram stream is told apart by its id, and the lifetimes.
/// </remarks>
public sealed class WorkspaceStreamFlowTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private const string DeveloperUsername = "admin";
    private const string DeveloperCredential = "changeme";
    private const string SessionTokenHeader = "session-token";
    private static readonly TimeSpan MessageTimeout = TimeSpan.FromSeconds(10);

    private readonly WebApplicationFactory<Program> _factory;
    private readonly string _appDataRoot;
    private readonly string _projectFolder;

    public WorkspaceStreamFlowTests(WebApplicationFactory<Program> baseFactory)
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
                // The problem cache lives and dies with this test, as in the other flow tests.
                services.RemoveAll<Problems.IProblemStore>();
                services.AddSingleton<Problems.IProblemStore>(provider => new Problems.ProblemStore(
                    _appDataRoot,
                    provider.GetRequiredService<DiagramFileRouter>(),
                    provider.GetRequiredService<DiagramValidators>()));
            });
        });
    }

    public void Dispose()
    {
        _factory.Dispose();
        TestFolder.TryDelete(_appDataRoot);
    }

    [Fact]
    public async Task Watch_CarriesTheContextTheHierarchyAndADiagram_OnOneStream()
    {
        // Arrange.
        await File.WriteAllTextAsync(IoPath.Combine(_projectFolder, "notes.txt"), "one\r\ntwo\r\n", TestContext.Current.CancellationToken);
        var session = await OpenSessionAsync();
        using var cts = CreateMessageTimeout();
        using var watch = session.Workspace.Watch(new WatchWorkspaceRequest { ProjectId = session.ProjectId, WatchId = session.WatchId }, session.Headers, cancellationToken: TestContext.Current.CancellationToken);

        // Act and assert, step by step. The context's baseline comes first: it is the client's sign
        // that the connection is registered and a diagram may be opened on it.
        var baseline = await ReadAsync(watch.ResponseStream, cts.Token);
        Assert.Equal(WorkspaceMessage.MessageOneofCase.Context, baseline.MessageCase);

        // A diagram joins the stream through the unary call, under the client's id.
        Documents.Wire.ShortGuid streamId = ShortGuid.NewShortGuid();
        await session.Workspace.OpenDiagramAsync(
            new OpenDiagramStreamRequest { StreamId = streamId, Request = OpenRequest(session, "notes.txt") },
            session.Headers,
            cancellationToken: TestContext.Current.CancellationToken);
        var delta = await ReadUntilAsync(watch.ResponseStream, message => message.MessageCase == WorkspaceMessage.MessageOneofCase.Diagram, cts.Token);
        Assert.Equal(streamId, delta.Diagram.StreamId);
        Assert.Equal(DiagramStreamMessage.EventOneofCase.Delta, delta.Diagram.EventCase);
        Assert.Equal(1, Connections().Find((ShortGuid)session.WatchId)?.DiagramStreamCount);

        // The hierarchy's changes ride the same stream: list the root so the entry is known, then
        // change the disk underneath it.
        await session.Hierarchy.ListEntriesAsync(new ListEntriesRequest { ProjectId = session.ProjectId, WatchId = session.WatchId }, session.Headers, cancellationToken: TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(IoPath.Combine(_projectFolder, "later.txt"), "", TestContext.Current.CancellationToken);
        var change = await ReadUntilAsync(
            watch.ResponseStream,
            message => message.MessageCase == WorkspaceMessage.MessageOneofCase.Hierarchy && message.Hierarchy.Change.Created?.Entry.Name == "later.txt",
            cts.Token);
        Assert.Equal("later.txt", change.Hierarchy.Change.Created.Entry.Name);

        // Closing the diagram stops its pump and leaves the connection open.
        await session.Workspace.CloseDiagramAsync(new CloseDiagramStreamRequest { WatchId = session.WatchId, StreamId = streamId }, session.Headers, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(await EventuallyAsync(() => Connections().Find((ShortGuid)session.WatchId)?.DiagramStreamCount == 0), "The diagram stream was still running after it was closed.");
        Assert.NotNull(Connections().Find((ShortGuid)session.WatchId));
    }

    [Fact]
    public async Task EndingTheWatch_EndsEveryDiagramStreamItCarried()
    {
        // Arrange.
        await File.WriteAllTextAsync(IoPath.Combine(_projectFolder, "notes.txt"), "one\r\n", TestContext.Current.CancellationToken);
        var session = await OpenSessionAsync();
        using var cts = CreateMessageTimeout();
        using var watchCancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var watch = session.Workspace.Watch(new WatchWorkspaceRequest { ProjectId = session.ProjectId, WatchId = session.WatchId }, session.Headers, cancellationToken: watchCancellation.Token);
        await ReadAsync(watch.ResponseStream, cts.Token);
        await session.Workspace.OpenDiagramAsync(
            new OpenDiagramStreamRequest { StreamId = ShortGuid.NewShortGuid(), Request = OpenRequest(session, "notes.txt") },
            session.Headers,
            cancellationToken: TestContext.Current.CancellationToken);
        var connection = Connections().Find((ShortGuid)session.WatchId);
        Assert.Equal(1, connection?.DiagramStreamCount);

        // Act: the tab goes away.
        await watchCancellation.CancelAsync();
        watch.Dispose();

        // Assert: the connection is gone, and so is the pump it carried - not merely forgotten.
        Assert.True(await EventuallyAsync(() => Connections().Find((ShortGuid)session.WatchId) is null), "The connection outlived its stream.");
        Assert.True(await EventuallyAsync(() => connection!.DiagramStreamCount == 0), "A diagram stream kept running after the stream carrying it ended.");
    }

    [Fact]
    public async Task OpenDiagram_ForAFileThatIsNotThere_IsRefusedWithThePermanentCode()
    {
        // Arrange.
        var session = await OpenSessionAsync();
        using var cts = CreateMessageTimeout();
        using var watch = session.Workspace.Watch(new WatchWorkspaceRequest { ProjectId = session.ProjectId, WatchId = session.WatchId }, session.Headers, cancellationToken: TestContext.Current.CancellationToken);
        await ReadAsync(watch.ResponseStream, cts.Token);

        // Act.
        var refusal = await Assert.ThrowsAsync<RpcException>(async () => await session.Workspace.OpenDiagramAsync(
            new OpenDiagramStreamRequest { StreamId = ShortGuid.NewShortGuid(), Request = OpenRequest(session, "missing.txt") },
            session.Headers,
            cancellationToken: TestContext.Current.CancellationToken));

        // Assert: the same code DiagramService.Open answers, which is what the client reads as
        // "this diagram cannot be opened here" rather than as a dropped connection.
        Assert.Equal(PermanentRefusal.CannotOpen, refusal.StatusCode);
        Assert.Equal(0, Connections().Find((ShortGuid)session.WatchId)?.DiagramStreamCount);
    }

    [Fact]
    public async Task OpenDiagram_WithNoWatchOpen_IsTransient()
    {
        // Arrange.
        await File.WriteAllTextAsync(IoPath.Combine(_projectFolder, "notes.txt"), "one\r\n", TestContext.Current.CancellationToken);
        var session = await OpenSessionAsync();

        // Act.
        var refusal = await Assert.ThrowsAsync<RpcException>(async () => await session.Workspace.OpenDiagramAsync(
            new OpenDiagramStreamRequest { StreamId = ShortGuid.NewShortGuid(), Request = OpenRequest(session, "notes.txt") },
            session.Headers,
            cancellationToken: TestContext.Current.CancellationToken));

        // Assert: Unavailable, which the client retries - never a permanent code for a connection
        // that is merely not open yet.
        Assert.Equal(StatusCode.Unavailable, refusal.StatusCode);
    }

    private WorkspaceConnections Connections() => _factory.Services.GetRequiredService<WorkspaceConnections>();

    private static OpenDiagramRequest OpenRequest(Session session, string fileName)
    {
        var path = new Path();
        path.Segments.Add(fileName);
        return new OpenDiagramRequest { ProjectId = session.ProjectId, WatchId = session.WatchId, Path = path };
    }

    private static CancellationTokenSource CreateMessageTimeout()
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cts.CancelAfter(MessageTimeout);
        return cts;
    }

    private static async Task<WorkspaceMessage> ReadAsync(IAsyncStreamReader<WorkspaceMessage> stream, CancellationToken cancellationToken)
    {
        Assert.True(await stream.MoveNext(cancellationToken), "The workspace stream ended.");
        return stream.Current;
    }

    private static async Task<WorkspaceMessage> ReadUntilAsync(IAsyncStreamReader<WorkspaceMessage> stream, Func<WorkspaceMessage, bool> wanted, CancellationToken cancellationToken)
    {
        while (true)
        {
            var message = await ReadAsync(stream, cancellationToken);
            if (wanted(message))
            {
                return message;
            }
        }
    }

    private static async Task<bool> EventuallyAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + MessageTimeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return true;
            }

            await Task.Delay(25, TestContext.Current.CancellationToken);
        }

        return condition();
    }

    private async Task<Session> OpenSessionAsync()
    {
        var httpClient = _factory.CreateDefaultClient();
        var channel = GrpcChannel.ForAddress(httpClient.BaseAddress!, new GrpcChannelOptions { HttpClient = httpClient });
        var authClient = new AuthenticationService.AuthenticationServiceClient(channel);
        var login = await authClient.LoginAsync(new LoginRequest { Username = DeveloperUsername, Credential = DeveloperCredential }, cancellationToken: TestContext.Current.CancellationToken);
        var headers = new Metadata { { SessionTokenHeader, login.Session.Value } };

        var pathMessage = new Path();
        pathMessage.Segments.AddRange(_projectFolder.Split(IoPath.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries));
        var added = await new ProjectService.ProjectServiceClient(channel).AddProjectAsync(new AddProjectRequest { Path = pathMessage }, headers, cancellationToken: TestContext.Current.CancellationToken);

        return new Session(
            headers,
            added.Added.Id,
            ShortGuid.NewShortGuid(),
            new WorkspaceService.WorkspaceServiceClient(channel),
            new HierarchyService.HierarchyServiceClient(channel));
    }

    private sealed record Session(
        Metadata Headers,
        Documents.Wire.ShortGuid ProjectId,
        Documents.Wire.ShortGuid WatchId,
        WorkspaceService.WorkspaceServiceClient Workspace,
        HierarchyService.HierarchyServiceClient Hierarchy);
}
