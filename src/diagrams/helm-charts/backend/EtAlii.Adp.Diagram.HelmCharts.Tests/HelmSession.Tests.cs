using EtAlii.Adp.Backend;
using EtAlii.Adp.Backend.Diagrams;
using EtAlii.Adp.Backend.Hierarchy;

using Microsoft.Extensions.DependencyInjection;

using Xunit;

using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Diagram.HelmCharts.Tests;

/// <summary>
/// The session (Requirements 6.2-6.4, 2.2): whole chart at baseline, deltas from the watcher,
/// reparenting refused, and the one edit - a reposition - dispatched to core's layout command,
/// undoable, with the chart's own files never changing by a byte.
/// </summary>
public class HelmSessionTests : IAsyncDisposable, IDisposable
{
    private static readonly TimeSpan SettleDelay = TimeSpan.FromMilliseconds(150);
    private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(10);

    private readonly string _root;
    private readonly string _adpPath;
    private readonly ServiceProvider _provider;
    private readonly HelmChartStore _store;

    public HelmSessionTests()
    {
        _root = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        File.WriteAllText(IoPath.Combine(_root, "Chart.yaml"), "apiVersion: v2\nname: sessioned\nversion: 1.0.0\n");
        File.WriteAllText(IoPath.Combine(_root, "values.yaml"), "replicaCount: 1\n");
        _adpPath = IoPath.Combine(_root, "helm-chart.adp");
        File.WriteAllText(_adpPath, "helm/chart\r\n");

        _provider = new ServiceCollection()
            .AddCommands()
            .AddHelmCharts()
            .BuildServiceProvider();
        _store = new HelmChartStore(new HelmChartReader(), SettleDelay);
    }

    public void Dispose()
    {
        _provider.Dispose();
        _store.Dispose();
        TestFolder.TryDelete(_root);
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    private HelmSession Session(bool withHistory = true) => new(
        _root,
        _adpPath,
        _store,
        new HelmElementMapper(),
        withHistory ? _provider.GetRequiredService<IHistoryStackStore>().Get(_root) : null);

    [Fact]
    public void Baseline_DeliversTheWholeChart()
    {
        // Arrange & Act.
        var session = Session();
        var deltas = session.Baseline();

        // Assert.
        var add = Assert.IsType<DiagramAddDelta>(Assert.Single(deltas));
        Assert.Contains(add.Elements, element => element.Id == "chart");
        Assert.Contains(add.Elements, element => element.Id == "values:values.yaml");
        // The viewport has nothing to add - a chart is bounded.
        Assert.Empty(session.UpdateView(new DiagramViewport(0, 0, 10_000, 10_000)));
    }

    [Fact]
    public async Task Reparenting_IsRefusedWithASentence()
    {
        // Arrange.
        await using var session = Session();

        // Act.
        var answer = await session.MoveElementAsync("chart", "values:values.yaml", 0, CancellationToken.None);

        // Assert.
        Assert.NotEqual(string.Empty, answer);
    }

    [Fact]
    public async Task WithoutHistory_ARepositionSaysReadOnly()
    {
        // Arrange.
        await using var session = Session(withHistory: false);

        // Act.
        var answer = await session.MoveElementToAsync("chart", 10, 20, CancellationToken.None);

        // Assert.
        Assert.Equal("This diagram is read-only.", answer);
    }

    [Fact]
    public async Task AnEdge_HasNoPositionOfItsOwn()
    {
        // Arrange.
        await using var session = Session();

        // Act.
        var answer = await session.MoveElementToAsync("edge:a|Declares|b|", 10, 20, CancellationToken.None);

        // Assert.
        Assert.NotEqual(string.Empty, answer);
    }

    [Fact]
    public async Task AReposition_IsOneUndoAway_AndTheChartNeverChanges()
    {
        // Arrange.
        var chartBytes = File.ReadAllBytes(IoPath.Combine(_root, "Chart.yaml"));
        var valuesBytes = File.ReadAllBytes(IoPath.Combine(_root, "values.yaml"));
        await using var session = Session();
        session.Baseline();
        var history = _provider.GetRequiredService<IHistoryStackStore>().Get(_root);

        // Act.
        var answer = await session.MoveElementToAsync("chart", 321, 123, CancellationToken.None);

        // Assert.
        Assert.Equal(string.Empty, answer);
        var stored = RegistrationLayout.Read(_adpPath);
        Assert.True(stored.TryGetValue("chart", out var position));
        Assert.Equal(321, position.X);
        Assert.Equal(123, position.Y);

        // Undo restores the registration to having no entry at all (the first move's inverse).
        var undo = await history.UndoAsync(CancellationToken.None);
        Assert.True(undo.IsSuccess);
        Assert.Empty(RegistrationLayout.Read(_adpPath));

        // And the chart's own files did not change by a byte, either way (Requirement 7.1).
        Assert.Equal(chartBytes, File.ReadAllBytes(IoPath.Combine(_root, "Chart.yaml")));
        Assert.Equal(valuesBytes, File.ReadAllBytes(IoPath.Combine(_root, "values.yaml")));
    }

    [Fact]
    public async Task AStoredPosition_WinsOverTheComputedOne_SizeStaysMeasured()
    {
        // Arrange.
        await using var session = Session();
        session.Baseline();

        // Act.
        await session.MoveElementToAsync("chart", 555, 444, CancellationToken.None);
        var rendered = Assert.IsType<DiagramAddDelta>(Assert.Single(session.Baseline()));

        // Assert.
        var chart = Assert.Single(rendered.Elements, element => element.Id == "chart");
        Assert.Equal(555, chart.X);
        Assert.Equal(444, chart.Y);
        var payload = Wire.HelmElementPayload.Parser.ParseFrom(chart.Payload.Span);
        Assert.True(payload.Width > 0); // the size stayed the layout's measurement
    }

    [Fact]
    public async Task ADiskChange_ArrivesAsDeltas()
    {
        // Arrange.
        await using var session = Session();
        session.Baseline();
        using var pushed = new ManualResetEventSlim();
        IReadOnlyList<DiagramDelta>? deltas = null;
        session.Changed += (_, e) =>
        {
            deltas = e.Deltas;
            pushed.Set();
        };

        // Act.
        File.WriteAllText(IoPath.Combine(_root, "values-prod.yaml"), "replicaCount: 3\n");

        // Assert.
        Assert.True(pushed.Wait(WaitLimit, TestContext.Current.CancellationToken), "The session never pushed the change.");
        Assert.NotNull(deltas);
        var add = deltas.OfType<DiagramAddDelta>().Single();
        Assert.Contains(add.Elements, element => element.Id == "values:values-prod.yaml");
    }
}
