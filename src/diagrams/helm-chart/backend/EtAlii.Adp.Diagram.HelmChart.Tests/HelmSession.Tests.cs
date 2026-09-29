using EtAlii.Adp.Hierarchy;
using EtAlii.Adp.History;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Diagram.HelmChart.Tests;

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
            .AddCommands().AddHierarchyCommandHandlers()
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
        // A viewport that admits the whole chart changes nothing: everything the baseline
        // delivered is still visible, so nothing appeared and nothing left.
        Assert.Empty(session.UpdateView(new DiagramViewport(0, 0, 10_000, 10_000)));
    }

    /// <summary>
    /// The loop this module exists to close (view-delta-adoption Requirements 1.2, 1.3): a
    /// changed viewport answers with what came into view and what left it. Written against
    /// element positions rather than literal coordinates, so a layout change moves the test
    /// with the code instead of breaking it.
    /// </summary>
    [Fact]
    public void UpdateView_AnswersAChangedViewportWithWhatAppearedAndWhatLeft()
    {
        // Arrange: the whole chart, and the two elements furthest apart on the x axis - the
        // layout lays its kinds out in columns, so those two are never in one narrow viewport.
        var session = Session();
        var all = Assert.IsType<DiagramAddDelta>(Assert.Single(session.Baseline())).Elements;
        var leftmost = all.MinBy(element => element.X)!;
        var rightmost = all.MaxBy(element => element.X)!;
        Assert.NotEqual(leftmost.Id, rightmost.Id);

        // Act: look at the left edge only, then at the right edge only.
        var narrowed = session.UpdateView(Around(leftmost));
        var moved = session.UpdateView(Around(rightmost));

        // Assert: narrowing removed the far element and added nothing...
        Assert.DoesNotContain(narrowed.OfType<DiagramAddDelta>().SelectMany(delta => delta.Elements), element => element.Id == rightmost.Id);
        Assert.Contains(rightmost.Id, narrowed.OfType<DiagramRemoveDelta>().SelectMany(delta => delta.ElementIds));

        // ...and moving across removed the near element and added the far one, in that order:
        // removals first, the one order every module sends (backend-centralization R4.5).
        Assert.Contains(moved.OfType<DiagramAddDelta>().SelectMany(delta => delta.Elements), element => element.Id == rightmost.Id);
        Assert.Contains(leftmost.Id, moved.OfType<DiagramRemoveDelta>().SelectMany(delta => delta.ElementIds));
        Assert.IsType<DiagramRemoveDelta>(moved[0]);
        Assert.IsType<DiagramAddDelta>(moved[1]);
    }

    /// <summary>A viewport tight around one element, in the module's own units.</summary>
    private static DiagramViewport Around(DiagramElement element) =>
        new(element.X - 1, element.Y - 1, element.X + 1, element.Y + 1);

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
        var chartBytes = await File.ReadAllBytesAsync(IoPath.Combine(_root, "Chart.yaml"), TestContext.Current.CancellationToken);
        var valuesBytes = await File.ReadAllBytesAsync(IoPath.Combine(_root, "values.yaml"), TestContext.Current.CancellationToken);
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
        Assert.Equal(chartBytes, await File.ReadAllBytesAsync(IoPath.Combine(_root, "Chart.yaml"), TestContext.Current.CancellationToken));
        Assert.Equal(valuesBytes, await File.ReadAllBytesAsync(IoPath.Combine(_root, "values.yaml"), TestContext.Current.CancellationToken));
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
            // ReSharper disable once AccessToDisposedClosure
            // Reason: This works.
            pushed.Set();
        };

        // Act.
        await File.WriteAllTextAsync(IoPath.Combine(_root, "values-prod.yaml"), "replicaCount: 3\n", TestContext.Current.CancellationToken);

        // Assert.
        Assert.True(pushed.Wait(WaitLimit, TestContext.Current.CancellationToken), "The session never pushed the change.");
        Assert.NotNull(deltas);
        var add = deltas.OfType<DiagramAddDelta>().Single();
        Assert.Contains(add.Elements, element => element.Id == "values:values-prod.yaml");
    }

    /// <summary>
    /// A chart change sends what changed, not the whole chart again (backend-centralization
    /// R4.2): every element the push adds is new or differs from what this connection already
    /// holds, and an element the change did not touch is not resent.
    /// </summary>
    [Fact]
    public async Task ADiskChange_ResendsOnlyWhatChanged()
    {
        // Arrange.
        await using var session = Session();
        var delivered = Assert.IsType<DiagramAddDelta>(Assert.Single(session.Baseline())).Elements
            .ToDictionary(element => element.Id, StringComparer.Ordinal);
        using var pushed = new ManualResetEventSlim();
        IReadOnlyList<DiagramDelta>? deltas = null;
        session.Changed += (_, e) =>
        {
            deltas = e.Deltas;
            // ReSharper disable once AccessToDisposedClosure
            // Reason: This works.
            pushed.Set();
        };

        // Act.
        await File.WriteAllTextAsync(IoPath.Combine(_root, "values-prod.yaml"), "replicaCount: 3\n", TestContext.Current.CancellationToken);

        // Assert.
        Assert.True(pushed.Wait(WaitLimit, TestContext.Current.CancellationToken), "The session never pushed the change.");
        Assert.NotNull(deltas);
        var added = deltas.OfType<DiagramAddDelta>().SelectMany(delta => delta.Elements).ToArray();
        Assert.Contains(added, element => element.Id == "values:values-prod.yaml");
        Assert.DoesNotContain(added, element =>
            delivered.TryGetValue(element.Id, out var was) && DiagramDiff.Same(was, element));
        // The chart node sits where it sat, so it is the element a resend-everything would send.
        Assert.DoesNotContain(added, element => element.Id == "chart");
    }
}
