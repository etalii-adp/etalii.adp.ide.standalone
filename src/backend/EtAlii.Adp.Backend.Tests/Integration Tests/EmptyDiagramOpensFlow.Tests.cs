using EtAlii.Adp.Diagram.Wire;
using EtAlii.Adp.TestSupport.Host;
using Grpc.Core;
using Xunit;
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here
using Path = EtAlii.Adp.Documents.Wire.Path;

namespace EtAlii.Adp.Backend.Tests;

/// <summary>
/// A diagram with nothing in it still answers its open: the stream's first message arrives, an
/// empty add, so the client leaves "Opening…" for the diagram's empty state.
/// </summary>
/// <remarks>
/// <para>
/// <b>The bug.</b> The client knows an open has answered only when the first delta arrives. A
/// session whose baseline is no deltas at all - a Helm chart registered over a folder without
/// <c>Chart.yaml</c>, which the problems panel meanwhile reported as <c>helm.not-a-chart</c> -
/// sent nothing, and its tab said "Opening…" until something in the folder changed.
/// </para>
/// <para>
/// <b>Why through a module and not a stub.</b> The fix is in core's pump and covers every module,
/// but the case that was seen is this one, through the real host and the real router. A stub
/// session would prove the pump writes an empty add for an empty list; this proves that the
/// diagram that hung no longer does.
/// </para>
/// </remarks>
public sealed class EmptyDiagramOpensFlowTests : IClassFixture<HostFixture>
{
    private readonly HostFixture _host;
    private readonly string _projectFolder;

    public EmptyDiagramOpensFlowTests(HostFixture host)
    {
        _host = host;
        _projectFolder = host.NewProjectFolder();

        // A Helm chart registration over a folder that is not a chart: no Chart.yaml beside it.
        var folder = IoPath.Combine(_projectFolder, "not-a-chart");
        Directory.CreateDirectory(folder);
        File.WriteAllText(IoPath.Combine(folder, "not-a-chart.adp"), "helm/chart\r\n");
    }

    [Fact]
    public async Task AHelmChartOverAFolderWithoutChartYaml_AnswersItsOpenWithAnEmptyAdd()
    {
        // Arrange.
        using var session = await FlowSession.OpenAsync(_host, _projectFolder, TestContext.Current.CancellationToken);
        var client = new DiagramService.DiagramServiceClient(session.Channel);
        using var cts = FlowSession.CreateMessageTimeout(TestContext.Current.CancellationToken);
        using var call = client.Open(
            new OpenDiagramRequest { ProjectId = session.ProjectId, WatchId = ShortGuid.NewShortGuid(), Path = Path.Of("not-a-chart", "not-a-chart.adp") },
            session.Headers,
            cancellationToken: cts.Token);

        // Act. Before the fix nothing arrived: the read waited out the message timeout.
        var answered = await FirstMessageArrivesAsync(call.ResponseStream, cts.Token);

        // Assert.
        Assert.True(answered, "An empty diagram's open stream sent nothing, so the client would show \"Opening…\" for ever.");
        Assert.Equal(Delta.ActionOneofCase.Add, call.ResponseStream.Current.ActionCase);
        Assert.Empty(call.ResponseStream.Current.Add.Elements);
    }

    private static async Task<bool> FirstMessageArrivesAsync(IAsyncStreamReader<Delta> stream, CancellationToken cancellationToken)
    {
        try
        {
            return await stream.MoveNext(cancellationToken);
        }
        catch (RpcException exception) when (exception.StatusCode == StatusCode.Cancelled)
        {
            // The message timeout: nothing came.
            return false;
        }
    }
}
