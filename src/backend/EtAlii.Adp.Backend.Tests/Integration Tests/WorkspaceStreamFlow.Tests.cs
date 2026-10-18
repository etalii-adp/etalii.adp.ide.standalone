using EtAlii.Adp.Diagram.Wire;
using EtAlii.Adp.Hierarchy;
using EtAlii.Adp.Hierarchy.Wire;
using EtAlii.Adp.Projects;
using EtAlii.Adp.TestSupport.Host;
using EtAlii.Adp.Workspace;
using Grpc.Core;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using HierarchyService = EtAlii.Adp.Hierarchy.Wire.HierarchyService;
using IoPath = System.IO.Path;
using Path = EtAlii.Adp.Documents.Wire.Path;
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
public sealed class WorkspaceStreamFlowTests(HostFixture host) : IClassFixture<HostFixture>
{
    private readonly string _projectFolder = host.NewProjectFolder();

    [Fact]
    public async Task Watch_CarriesTheContextTheHierarchyAndADiagram_OnOneStream()
    {
        // Arrange.
        await File.WriteAllTextAsync(IoPath.Combine(_projectFolder, "notes.txt"), "one\r\ntwo\r\n", TestContext.Current.CancellationToken);
        var session = await OpenSessionAsync();
        using var cts = FlowSession.CreateMessageTimeout(TestContext.Current.CancellationToken);
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
        var delta = await watch.ResponseStream.FirstAsync(message => message.MessageCase == WorkspaceMessage.MessageOneofCase.Diagram, cts.Token);
        Assert.Equal(streamId, delta.Diagram.StreamId);
        Assert.Equal(DiagramStreamMessage.EventOneofCase.Delta, delta.Diagram.EventCase);
        Assert.Equal(1, Connections().Find((ShortGuid)session.WatchId, session.UserId)?.StreamCount);

        // The hierarchy's changes ride the same stream: list the root so the entry is known, then
        // change the disk underneath it.
        await session.Hierarchy.ListEntriesAsync(new ListEntriesRequest { ProjectId = session.ProjectId, WatchId = session.WatchId }, session.Headers, cancellationToken: TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(IoPath.Combine(_projectFolder, "later.txt"), "", TestContext.Current.CancellationToken);
        var change = await watch.ResponseStream.FirstAsync(
            message => message.MessageCase == WorkspaceMessage.MessageOneofCase.Hierarchy && message.Hierarchy.Change.Created?.Entry.Name == "later.txt",
            cts.Token);
        Assert.Equal("later.txt", change.Hierarchy.Change.Created.Entry.Name);

        // Closing the diagram stops its pump and leaves the connection open.
        await session.Workspace.CloseDiagramAsync(new CloseDiagramStreamRequest { WatchId = session.WatchId, StreamId = streamId }, session.Headers, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(await Eventually.TrueAsync(() => Connections().Find((ShortGuid)session.WatchId, session.UserId)?.StreamCount == 0, TestContext.Current.CancellationToken, FlowConstants.MessageTimeout), "The diagram stream was still running after it was closed.");
        Assert.NotNull(Connections().Find((ShortGuid)session.WatchId, session.UserId));
    }

    [Fact]
    public async Task EndingTheWatch_EndsEveryDiagramStreamItCarried()
    {
        // Arrange.
        await File.WriteAllTextAsync(IoPath.Combine(_projectFolder, "notes.txt"), "one\r\n", TestContext.Current.CancellationToken);
        var session = await OpenSessionAsync();
        using var cts = FlowSession.CreateMessageTimeout(TestContext.Current.CancellationToken);
        using var watchCancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var watch = session.Workspace.Watch(new WatchWorkspaceRequest { ProjectId = session.ProjectId, WatchId = session.WatchId }, session.Headers, cancellationToken: watchCancellation.Token);
        await ReadAsync(watch.ResponseStream, cts.Token);
        await session.Workspace.OpenDiagramAsync(
            new OpenDiagramStreamRequest { StreamId = ShortGuid.NewShortGuid(), Request = OpenRequest(session, "notes.txt") },
            session.Headers,
            cancellationToken: TestContext.Current.CancellationToken);
        var connection = Connections().Find((ShortGuid)session.WatchId, session.UserId);
        Assert.Equal(1, connection?.StreamCount);

        // Act: the tab goes away.
        await watchCancellation.CancelAsync();
        watch.Dispose();

        // Assert: the connection is gone, and so is the pump it carried - not merely forgotten.
        Assert.True(await Eventually.TrueAsync(() => Connections().Find((ShortGuid)session.WatchId, session.UserId) is null, TestContext.Current.CancellationToken, FlowConstants.MessageTimeout), "The connection outlived its stream.");
        Assert.True(await Eventually.TrueAsync(() => connection!.StreamCount == 0, TestContext.Current.CancellationToken, FlowConstants.MessageTimeout), "A diagram stream kept running after the stream carrying it ended.");
    }

    [Fact]
    public async Task EndingTheWatch_ReleasesTheHierarchyModelItHeld()
    {
        // Arrange: the watch's model is claimed in the connection's scope as the stream opens.
        var session = await OpenSessionAsync();
        using var cts = FlowSession.CreateMessageTimeout(TestContext.Current.CancellationToken);
        using var watchCancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var watch = session.Workspace.Watch(new WatchWorkspaceRequest { ProjectId = session.ProjectId, WatchId = session.WatchId }, session.Headers, cancellationToken: watchCancellation.Token);
        await ReadAsync(watch.ResponseStream, cts.Token);
        var models = host.Services.GetRequiredService<IHierarchyModelStore>();
        var root = ProjectRoot.From(_projectFolder);
        var held = models.GetOrCreate((ShortGuid)session.WatchId, root);

        // Act: the tab goes away.
        await watchCancellation.CancelAsync();
        watch.Dispose();

        // Assert: the scope let the model go, so asking again builds another.
        Assert.True(
            await Eventually.TrueAsync(() => !ReferenceEquals(held, models.GetOrCreate((ShortGuid)session.WatchId, root)), TestContext.Current.CancellationToken, FlowConstants.MessageTimeout),
            "The watch's hierarchy model outlived its stream.");
    }

    [Fact]
    public async Task OpenDiagram_ForAFileThatIsNotThere_IsRefusedWithThePermanentCode()
    {
        // Arrange.
        var session = await OpenSessionAsync();
        using var cts = FlowSession.CreateMessageTimeout(TestContext.Current.CancellationToken);
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
        Assert.Equal(0, Connections().Find((ShortGuid)session.WatchId, session.UserId)?.StreamCount);
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

    private WorkspaceConnections Connections() => host.Services.GetRequiredService<WorkspaceConnections>();

    private static OpenDiagramRequest OpenRequest(Session session, string fileName)
    {
        var path = Path.Of(fileName);
        return new OpenDiagramRequest { ProjectId = session.ProjectId, WatchId = session.WatchId, Path = path };
    }

    private static async Task<WorkspaceMessage> ReadAsync(IAsyncStreamReader<WorkspaceMessage> stream, CancellationToken cancellationToken)
    {
        Assert.True(await stream.MoveNext(cancellationToken), "The workspace stream ended.");
        return stream.Current;
    }

    private async Task<Session> OpenSessionAsync()
    {
        // The connection stays open for the rest of the test: the clients below ride its channel.
        var flow = await FlowSession.OpenAsync(host, _projectFolder, TestContext.Current.CancellationToken);
        return new Session(
            flow.Headers,
            flow.ProjectId,
            flow.WatchId,
            new WorkspaceService.WorkspaceServiceClient(flow.Channel),
            flow.Hierarchy,
            flow.UserId);
    }

    private sealed record Session(
        Metadata Headers,
        Documents.Wire.ShortGuid ProjectId,
        Documents.Wire.ShortGuid WatchId,
        WorkspaceService.WorkspaceServiceClient Workspace,
        HierarchyService.HierarchyServiceClient Hierarchy,
        ShortGuid UserId);
}
