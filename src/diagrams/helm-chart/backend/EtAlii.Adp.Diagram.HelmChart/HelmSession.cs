using EtAlii.Adp.Hierarchy;
using EtAlii.Adp.History;
using Serilog;

namespace EtAlii.Adp.Diagram.HelmChart;

/// <summary>
/// One open helm chart diagram for one connection. Core owns the stream, the authorization and
/// the connection lifetime; this owns the folder-to-elements reading and expresses every
/// change as a <see cref="DiagramDelta"/>.
/// </summary>
/// <remarks>
/// <para>
/// The whole diagram is delivered at <see cref="Baseline"/>, and <see cref="UpdateView"/>
/// narrows it to what the reader is looking at: what newly falls inside the viewport is
/// added, what left is removed (view-delta-adoption Requirements 1.2 and 1.3). This replaces
/// an earlier judgement that a chart is bounded and so has nothing to virtualize - true of a
/// small chart, and false of one with enough dependencies to be worth panning around.
/// </para>
/// <para>
/// A drag never touches the chart: repositioning dispatches the core
/// <see cref="SetRegistrationLayoutCommand"/>, which writes the <c>.adp</c>'s <c>layout:</c>
/// block (Requirement 6.2). The registration sits inside the chart root, so the write comes
/// back through the folder watcher like any other change, and the re-render overlays the
/// authored position - one path for computed and dragged alike.
/// </para>
/// </remarks>
internal sealed class HelmSession : IDiagramSession
{
    private static readonly ILogger _logger = Log.ForContext<HelmSession>();

    private readonly string _folder;
    private readonly string _registrationPath;
    private readonly IHelmChartStore _store;
    private readonly HelmElementMapper _mapper;

    /// <summary>The project's history, so a drag is one undo away. Null makes the diagram fully read-only.</summary>
    private readonly IHistoryStack? _history;

    /// <summary>What this connection was last sent, so a change can be diffed against it.</summary>
    private IReadOnlyList<DiagramElement> _delivered = [];

    /// <summary>
    /// What this connection is looking at. Unbounded until the client reports, so a client
    /// that never reports keeps seeing the whole chart it was given at baseline.
    /// </summary>
    private DiagramViewport _viewport = DiagramViewport.Unbounded;

    public HelmSession(
        string folder,
        string registrationPath,
        IHelmChartStore store,
        HelmElementMapper mapper,
        IHistoryStack? history = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        ArgumentException.ThrowIfNullOrWhiteSpace(registrationPath);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(mapper);

        _folder = folder;
        _registrationPath = registrationPath;
        _store = store;
        _mapper = mapper;
        _history = history;

        _store.Acquire(_folder);
        _store.Changed += OnChartChanged;
    }

    /// <inheritdoc />
    public event EventHandler<DiagramDeltasEventArgs>? Changed;

    /// <inheritdoc />
    public IReadOnlyList<DiagramDelta> Baseline()
    {
        var elements = Render();
        _delivered = elements;

        return elements.Count > 0
            ? [new DiagramAddDelta(elements)]
            : [];
    }

    /// <inheritdoc />
    public IReadOnlyList<DiagramDelta> UpdateView(DiagramViewport viewport)
    {
        _viewport = viewport;
        var after = Render();

        // Diffed against what this connection holds, by the shared diff every module uses
        // (backend-centralization R4): a Remove for what left the view, then an Add for what
        // came into it - removals first, the one order R4.5 settles, where this used to add
        // first. It is also what a chart change diffs against next, so that diffs against the
        // narrowed set rather than against the whole diagram last seen at baseline.
        var deltas = DiagramDiff.Between(_delivered, after);
        _delivered = after;
        return deltas;
    }

    /// <summary>Refused: nothing in this diagram nests under a parent - its place is a position.</summary>
    public Task<string> MoveElementAsync(string elementId, string newParentId, int index, CancellationToken cancellationToken)
    {
        _ = elementId;
        _ = newParentId;
        _ = index;
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult("A chart element has no parent to move it under; dragging changes where it sits on the canvas.");
    }

    /// <summary>
    /// Stores an authored position in the registration's <c>layout:</c> block, as one undoable
    /// command - and never writes a chart file (Requirements 6.2, 6.4, 7.1).
    /// </summary>
    public async Task<string> MoveElementToAsync(string elementId, double x, double y, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(elementId);
        cancellationToken.ThrowIfCancellationRequested();

        if (_history is null)
        {
            return "This diagram is read-only.";
        }

        if (elementId.StartsWith("edge:", StringComparison.Ordinal))
        {
            // An edge has no position of its own - it follows its endpoints.
            return "That element is not something this diagram can move.";
        }

        var result = await _history.ExecuteAsync(
            new SetRegistrationLayoutCommand(_registrationPath, elementId, x, y),
            cancellationToken);

        return result.IsSuccess ? "" : result.Error;
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        _store.Changed -= OnChartChanged;
        _store.Release(_folder);
        return ValueTask.CompletedTask;
    }

    private IReadOnlyList<DiagramElement> Render()
    {
        var chart = _store.GetOrLoad(_folder);
        var graph = HelmGraph.Derive(chart);

        return _mapper.Visible(chart, graph, Boxes(chart, graph), _viewport);
    }

    /// <summary>
    /// Every box the chart currently has: computed positions first, stored ones winning
    /// element by element, sizes staying measured - a stale stored id simply has nothing to
    /// override (core's own overlay semantics). Every render - baseline, view report and chart
    /// change - goes through it, so no two of them can disagree about where an element sits.
    /// </summary>
    private IReadOnlyDictionary<string, HelmBox> Boxes(HelmChart chart, HelmGraph graph)
    {
        var boxes = HelmLayout.Compute(chart, graph);
        var stored = RegistrationLayout.Read(_registrationPath);
        if (stored.Count == 0)
        {
            return boxes;
        }

        var overlaid = new Dictionary<string, HelmBox>(boxes.Count, StringComparer.Ordinal);
        foreach (var (id, box) in boxes)
        {
            overlaid[id] = stored.TryGetValue(id, out var position)
                ? box with { X = position.X, Y = position.Y }
                : box;
        }

        return overlaid;
    }

    private void OnChartChanged(object? sender, HelmChartChangedEventArgs args)
    {
        if (!string.Equals(args.FolderPath, _folder, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        try
        {
            var current = Render();
            var deltas = DiagramDiff.Between(_delivered, current);
            _delivered = current;

            if (deltas.Count > 0)
            {
                Changed?.Invoke(this, new DiagramDeltasEventArgs(deltas));
            }
        }
        catch (Exception exception)
        {
            // A push that cannot be built loses that push and says so; the next change
            // catches the diagram up. Never the connection's death.
            _logger.Error(exception, "Re-rendering {Folder} after a change failed", _folder);
        }
    }
}
