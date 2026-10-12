using EtAlii.Adp.Context;
using EtAlii.Adp.Context.Wire;
using EtAlii.Adp.Diagram.Wire;
using EtAlii.Adp.Documents.Wire;
using EtAlii.Adp.Hierarchy;
using EtAlii.Adp.TestSupport.Host;
using Grpc.Core;
using Grpc.Net.Client;
using Xunit;
using ContextService = EtAlii.Adp.Context.Wire.ContextService;
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here
using Path = EtAlii.Adp.Documents.Wire.Path;
using Wire = EtAlii.Adp.Diagram.HelmChart.Wire;

namespace EtAlii.Adp.Backend.Tests;

/// <summary>
/// The helm chart diagram against the real host: a chart registered by one <c>.adp</c> inside
/// its own root, opened, its baseline read, the diagram following the folder as it changes -
/// and the layout-persistence promise end to end: move, reopen, undo, chart bytes untouched.
/// </summary>
/// <remarks>
/// The second folder-subject type over the same core seams the first proved: core routes the
/// registration as its own body, resolves the session factory by origin, and never learns
/// that the subject is a folder - while this type additionally writes authored positions into
/// the registration through core's own layout command.
/// </remarks>
public class HelmChartsFlowTests : IClassFixture<HostFixture>
{
    private readonly HostFixture _host;
    private readonly string _projectFolder;
    private readonly string _chart;

    public HelmChartsFlowTests(HostFixture host)
    {
        _host = host;
        _projectFolder = host.NewProjectFolder();
        _chart = IoPath.Combine(_projectFolder, "web-shop");

        // A real chart inside the project folder, registered the way Add would - the .adp
        // sits inside the chart root, beside Chart.yaml (Requirement 2.2).
        HelmFixture.CopyTo(_chart);
        File.WriteAllText(IoPath.Combine(_chart, "web-shop.adp"), "helm/chart\r\n");
    }

    [Fact]
    public async Task ARegisteredChart_OpensAndDeliversItsAnatomy()
    {
        // Arrange.
        using var session = await FlowSession.OpenAsync(_host, _projectFolder, TestContext.Current.CancellationToken);

        // Act.
        var elements = await BaselineAsync(session.Channel, session.Headers, session.ProjectId);

        // Assert.
        // Core routed a registration whose body is itself, resolved this module's session
        // factory by origin, and never learned that the subject is a folder.
        var ids = elements.Select(element => element.Id.Value).ToArray();
        Assert.Contains("chart", ids);
        Assert.Contains("values:values.yaml", ids);
        Assert.Contains("tpl:templates/deployment.yaml", ids);
        Assert.Contains("dep:cache", ids);
        Assert.Contains("sub:charts/redis", ids);
        Assert.Contains("lock:Chart.lock", ids);
        Assert.Contains(ids, id => id.StartsWith("edge:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ADependencysPayload_CarriesItsWiring()
    {
        // Arrange.
        using var session = await FlowSession.OpenAsync(_host, _projectFolder, TestContext.Current.CancellationToken);

        // Act.
        var elements = await BaselineAsync(session.Channel, session.Headers, session.ProjectId);
        var cache = elements.Single(element => element.Id.Value == "dep:cache");
        var payload = Wire.HelmElementPayload.Parser.ParseFrom(cache.Payload.Value.Span);

        // Assert.
        Assert.Equal("redis", payload.Dependency.ChartName);
        Assert.Equal("cache", payload.Dependency.Alias);
        Assert.True(payload.Dependency.Resolved);
        Assert.Equal(Wire.HelmConditionState.ConditionOn, payload.Dependency.ConditionState);
        Assert.Equal("17.3.2", payload.Dependency.PinnedVersion);
        Assert.True(payload.Width > 0);
    }

    [Fact]
    public async Task AChangeInTheChart_ReachesAnOpenDiagram()
    {
        // Arrange.
        using var session = await FlowSession.OpenAsync(_host, _projectFolder, TestContext.Current.CancellationToken);
        var client = new DiagramService.DiagramServiceClient(session.Channel);

        using var cts = FlowSession.CreateMessageTimeout(TestContext.Current.CancellationToken);
        using var call = client.Open(
            new OpenDiagramRequest { ProjectId = session.ProjectId, WatchId = ShortGuid.NewShortGuid(), Path = ChartPath() },
            session.Headers, cancellationToken: cts.Token);

        // The baseline first, so the change that follows is the one being observed.
        Assert.True(await call.ResponseStream.MoveNext(cts.Token));

        // Act.
        // A new override layer appears in the folder. Nothing about the .adp changed.
        await File.WriteAllTextAsync(
            IoPath.Combine(_chart, "values-prod.yaml"), "replicaCount: 5\n", cts.Token);

        // Assert.
        // Requirement 1.4: the diagram a reader leaves open stays true.
        var arrived = await WaitForElementAsync(call.ResponseStream, "values:values-prod.yaml", cts.Token);
        Assert.True(arrived, "The new values layer never reached the open diagram.");
    }

    [Fact]
    public async Task AReposition_LandsInTheAdp_SurvivesReopen_AndUndoReturnsIt_WithTheChartUntouchedThroughout()
    {
        // Arrange: the layout-persistence promise (Requirement 6), end to end. The diagram
        // must be open on the SAME connection, because core will not edit a document a caller
        // is not looking at.
        using var session = await FlowSession.OpenAsync(_host, _projectFolder, TestContext.Current.CancellationToken);
        var client = new DiagramService.DiagramServiceClient(session.Channel);
        var contextClient = session.Context;
        var watchId = ShortGuid.NewShortGuid();
        var adpPath = IoPath.Combine(_chart, "web-shop.adp");
        var adpBefore = await File.ReadAllTextAsync(adpPath, TestContext.Current.CancellationToken);
        var chartBefore = Snapshot(except: adpPath);

        using var cts = FlowSession.CreateMessageTimeout(TestContext.Current.CancellationToken);
        using var call = client.Open(
            new OpenDiagramRequest { ProjectId = session.ProjectId, WatchId = watchId, Path = ChartPath() },
            session.Headers, cancellationToken: cts.Token);
        await ReadAddAsync(call.ResponseStream, cts.Token);

        // Act: drag the chart node to an authored spot.
        var moved = await client.MoveElementAsync(
            new MoveElementRequest
            {
                ProjectId = session.ProjectId,
                WatchId = watchId,
                Path = ChartPath(),
                ElementId = "chart",
                Position = new Point2D { X = 480, Y = 260 },
            },
            session.Headers, cancellationToken: TestContext.Current.CancellationToken);

        // Assert: the position is in the registration, and nowhere near the chart (6.2, 7.1).
        Assert.Equal("", moved.Error);
        Assert.Contains("layout:", await File.ReadAllTextAsync(adpPath, TestContext.Current.CancellationToken), StringComparison.Ordinal);
        Assert.Equal(new RegistrationPosition(480, 260), RegistrationLayout.Read(adpPath)["chart"]);
        AssertUnchanged(chartBefore);

        // Act, continued: a fresh open sees the stored position overlaid (6.3).
        var reopened = await BaselineAsync(session.Channel, session.Headers, session.ProjectId);
        var chartNode = reopened.Single(element => element.Id.Value == "chart");
        Assert.Equal((480d, 260d), (chartNode.Position.X, chartNode.Position.Y));

        // Act, continued: one undo returns the registration byte for byte (6.4).
        var undone = await ExecuteProjectActionAsync(contextClient, session.ProjectId, watchId, session.Headers, HistoryContextActionProvider.UndoActionId);

        // Assert.
        Assert.True(undone.Accepted, undone.Error);
        Assert.Equal(adpBefore, await File.ReadAllTextAsync(adpPath, TestContext.Current.CancellationToken));
        AssertUnchanged(chartBefore);
    }

    // ---- helpers -------------------------------------------------------------------------

    private Dictionary<string, byte[]> Snapshot(string except) =>
        Directory.GetFiles(_chart, "*", SearchOption.AllDirectories)
            .Where(path => !string.Equals(path, except, StringComparison.OrdinalIgnoreCase))
            .ToDictionary(path => path, File.ReadAllBytes);

    private static void AssertUnchanged(Dictionary<string, byte[]> before)
    {
        foreach ((string path, byte[] bytes) in before)
        {
            Assert.True(bytes.SequenceEqual(File.ReadAllBytes(path)), $"{path} changed.");
        }
    }

    private static Path ChartPath()
    {
        var path = new Path();
        path.Segments.Add("web-shop");
        path.Segments.Add("web-shop.adp");
        return path;
    }

    private static async Task<bool> WaitForElementAsync(IAsyncStreamReader<Delta> stream, string elementId, CancellationToken cancellationToken)
    {
        try
        {
            while (await stream.MoveNext(cancellationToken))
            {
                if (stream.Current.ActionCase == Delta.ActionOneofCase.Add &&
                    stream.Current.Add.Elements.Any(element => element.Id.Value == elementId))
                {
                    return true;
                }
            }
        }
        catch (RpcException exception) when (exception.StatusCode == StatusCode.Cancelled)
        {
            // The deadline, which is this helper's way of giving up.
        }

        return false;
    }

    private async Task<IReadOnlyList<Element>> BaselineAsync(GrpcChannel channel, Metadata headers, Documents.Wire.ShortGuid projectId)
    {
        var client = new DiagramService.DiagramServiceClient(channel);
        using var cts = FlowSession.CreateMessageTimeout(TestContext.Current.CancellationToken);
        using var call = client.Open(
            new OpenDiagramRequest { ProjectId = projectId, WatchId = ShortGuid.NewShortGuid(), Path = ChartPath() },
            headers, cancellationToken: cts.Token);
        return await ReadAddAsync(call.ResponseStream, cts.Token);
    }

    private static async Task<IReadOnlyList<Element>> ReadAddAsync(
        IAsyncStreamReader<Delta> stream, CancellationToken cancellationToken)
    {
        try
        {
            while (await stream.MoveNext(cancellationToken))
            {
                if (stream.Current.ActionCase == Delta.ActionOneofCase.Add)
                {
                    return [.. stream.Current.Add.Elements];
                }
            }
        }
        catch (RpcException exception) when (exception.StatusCode == StatusCode.Cancelled)
        {
            // Reading stopped at the deadline, which is how this helper ends normally.
        }
        catch (OperationCanceledException)
        {
            // Same story, thrown the other way.
        }

        return [];
    }

    private static Task<ExecuteActionResponse> ExecuteProjectActionAsync(
        ContextService.ContextServiceClient contextClient,
        Documents.Wire.ShortGuid projectId,
        Documents.Wire.ShortGuid watchId,
        Metadata headers,
        string actionId) =>
        contextClient.ExecuteActionAsync(
            new ExecuteActionRequest
            {
                ProjectId = projectId,
                WatchId = watchId,
                Source = new ContextSource { Project = new Google.Protobuf.WellKnownTypes.Empty() },
                InteractionId = ShortGuid.NewShortGuid(),
                ActionId = actionId,
            },
            headers,
            cancellationToken: TestContext.Current.CancellationToken).ResponseAsync;

}
