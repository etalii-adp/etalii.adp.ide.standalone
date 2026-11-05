using EtAlii.Adp.Diagram.Wire;
using EtAlii.Adp.Projects;
using Grpc.Core;
using Xunit;
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here
using Path = EtAlii.Adp.Documents.Wire.Path;
using WorkspaceService = EtAlii.Adp.Diagram.Wire.WorkspaceService;

namespace EtAlii.Adp.Backend.Tests;

/// <summary>
/// A diagram whose baseline cannot be made is refused for good, in the module's own words, on both
/// ways a diagram is opened - so the client's tab says why instead of re-opening for ever.
/// </summary>
/// <remarks>
/// <para>
/// <b>The bug.</b> A Freeplane map with no root node opens - the session is made - but its
/// <c>Baseline()</c> throws <c>MindmapFormatException</c>. On the tab's one stream that failure was
/// sent as <c>ended</c>, which the client reads as a dropped stream and re-opens 500 ms later, so the
/// tab said "Opening…" for ever. On <c>DiagramService.Open</c> it ended the call as <c>Unknown</c>,
/// which the client also retries.
/// </para>
/// <para>
/// <b>Why through a module.</b> The refusal is core's and covers every module; the case that was
/// seen is this one, through the real host and the real router.
/// </para>
/// </remarks>
public sealed class UnreadableDiagramIsRefusedFlowTests : IClassFixture<HostFixture>
{
    private const string Reason = "root node";

    private readonly HostFixture _host;
    private readonly string _projectFolder;

    public UnreadableDiagramIsRefusedFlowTests(HostFixture host)
    {
        _host = host;
        _projectFolder = host.NewProjectFolder();

        // A map with no root node, registered as a mindmap the way ADP pairs a body with its .adp.
        File.WriteAllText(IoPath.Combine(_projectFolder, "unreadable.adp"), "freeplane/mindmap\r\n");
        File.WriteAllText(IoPath.Combine(_projectFolder, "unreadable.mm"), "<map version=\"freeplane 1.9.13\">\r\n</map>\r\n");
    }

    [Fact]
    public async Task OnTheTabsOneStream_AMapWithNoRootNode_IsRefusedForGood_NotEnded()
    {
        // Arrange.
        using var session = await FlowSession.OpenAsync(_host, _projectFolder, TestContext.Current.CancellationToken);
        var workspace = new WorkspaceService.WorkspaceServiceClient(session.Channel);
        using var cts = FlowSession.CreateMessageTimeout(TestContext.Current.CancellationToken);
        using var watch = workspace.Watch(new WatchWorkspaceRequest { ProjectId = session.ProjectId, WatchId = session.WatchId }, session.Headers, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(await watch.ResponseStream.MoveNext(cts.Token), "The workspace stream ended before its baseline.");
        Documents.Wire.ShortGuid streamId = ShortGuid.NewShortGuid();

        // Act. The open is answered: the session is made, and only its baseline fails.
        await workspace.OpenDiagramAsync(
            new OpenDiagramStreamRequest
            {
                StreamId = streamId,
                Request = new OpenDiagramRequest { ProjectId = session.ProjectId, WatchId = session.WatchId, Path = Path.Of("unreadable.mm") },
            },
            session.Headers,
            cancellationToken: TestContext.Current.CancellationToken);
        var message = await FirstDiagramMessageAsync(watch.ResponseStream, streamId, cts.Token);

        // Assert.
        Assert.Equal(DiagramStreamMessage.EventOneofCase.Refused, message.EventCase);
        Assert.True(PermanentRefusal.IsPermanent((StatusCode)message.Refused.Code), $"Refused with {(StatusCode)message.Refused.Code}, which the client retries.");
        Assert.StartsWith("'unreadable.mm' could not be read: ", message.Refused.Message, StringComparison.Ordinal);
        Assert.Contains(Reason, message.Refused.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(_projectFolder, message.Refused.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("read: unreadable.mm:", message.Refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OnItsOwnOpenCall_AMapWithNoRootNode_EndsWithAPermanentRefusal()
    {
        // Arrange.
        using var session = await FlowSession.OpenAsync(_host, _projectFolder, TestContext.Current.CancellationToken);
        var client = new DiagramService.DiagramServiceClient(session.Channel);
        using var cts = FlowSession.CreateMessageTimeout(TestContext.Current.CancellationToken);
        using var call = client.Open(
            new OpenDiagramRequest { ProjectId = session.ProjectId, WatchId = ShortGuid.NewShortGuid(), Path = Path.Of("unreadable.mm") },
            session.Headers,
            cancellationToken: cts.Token);

        // Act.
        var refusal = await Assert.ThrowsAsync<RpcException>(async () => await call.ResponseStream.MoveNext(cts.Token));

        // Assert.
        Assert.True(PermanentRefusal.IsPermanent(refusal.StatusCode), $"Ended with {refusal.StatusCode}, which the client retries.");
        Assert.Contains(Reason, refusal.Status.Detail, StringComparison.Ordinal);
    }

    private static async Task<DiagramStreamMessage> FirstDiagramMessageAsync(IAsyncStreamReader<WorkspaceMessage> stream, Documents.Wire.ShortGuid streamId, CancellationToken cancellationToken)
    {
        while (await stream.MoveNext(cancellationToken))
        {
            if (stream.Current.MessageCase == WorkspaceMessage.MessageOneofCase.Diagram && stream.Current.Diagram.StreamId.Equals(streamId))
            {
                return stream.Current.Diagram;
            }
        }

        throw new InvalidOperationException("The workspace stream ended before the diagram stream said anything.");
    }
}
