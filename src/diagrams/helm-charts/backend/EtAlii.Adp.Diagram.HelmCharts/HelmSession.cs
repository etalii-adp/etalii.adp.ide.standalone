using EtAlii.Adp.Backend;
using EtAlii.Adp.Backend.Diagrams;
using EtAlii.Adp.Backend.Hierarchy;

using Serilog;

namespace EtAlii.Adp.Diagram.HelmCharts;

/// <summary>
/// One open helm chart diagram for one connection. Core owns the stream, the authorization and
/// the connection lifetime; this owns the folder-to-elements reading and expresses every
/// change as a <see cref="DiagramDelta"/>.
/// </summary>
/// <remarks>
/// <para>
/// The whole diagram is delivered at <see cref="Baseline"/> and <see cref="UpdateView"/>
/// answers with nothing new: a chart is bounded, so there is nothing to virtualize (the
/// design's recorded fork from the Ansible module's viewport filter).
/// </para>
/// <para>
/// A drag never touches the chart: repositioning dispatches the core
/// <see cref="SetRegistrationLayoutCommand"/>, which writes the <c>.adp</c>'s <c>layout:</c>
/// block (Requirement 6.2). The registration sits inside the chart root, so the write comes
/// back through the folder watcher like any other change, and the re-render overlays the
/// authored position - one path for computed and dragged alike.
/// </para>
/// </remarks>
public sealed class HelmSession : IDiagramSession
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
        _ = viewport;
        return [];
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

        // Computed positions first; stored ones win element by element, sizes stay measured -
        // a stale stored id simply has nothing to override (core's own overlay semantics).
        var boxes = HelmLayout.Compute(chart, graph);
        var stored = RegistrationLayout.Read(_registrationPath);
        if (stored.Count > 0)
        {
            var overlaid = new Dictionary<string, HelmBox>(boxes.Count, StringComparer.Ordinal);
            foreach (var (id, box) in boxes)
            {
                overlaid[id] = stored.TryGetValue(id, out var position)
                    ? box with { X = position.X, Y = position.Y }
                    : box;
            }

            boxes = overlaid;
        }

        return _mapper.Elements(chart, graph, boxes);
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
            var deltas = _mapper.Diff(_delivered, current);
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
