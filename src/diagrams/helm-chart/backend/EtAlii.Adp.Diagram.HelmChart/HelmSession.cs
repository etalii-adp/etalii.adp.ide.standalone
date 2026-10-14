using EtAlii.Adp.Hierarchy;
using EtAlii.Adp.History;

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
/// <para>
/// <b>What this connection holds is kept by <see cref="DiagramDocumentChangeHandler"/></b>, as
/// every session's is (code-structure FR-031): the baseline, a viewport report and a change to the
/// chart all diff under its one lock against the one record of what was delivered.
/// </para>
/// </remarks>
internal sealed class HelmSession : IDiagramSession
{
    private readonly string _folder;
    private readonly string _registrationPath;
    private readonly IHelmChartStore _store;
    private readonly HelmElementMapper _mapper;

    /// <summary>The project's history, so a drag is one undo away. Null makes the diagram fully read-only.</summary>
    private readonly IHistoryStack? _history;

    /// <summary>The store's word for this folder, until the session goes.</summary>
    private readonly IDisposable _watch;

    private readonly DiagramDocumentChangeHandler _changes;

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
        _changes = new DiagramDocumentChangeHandler(_folder, Render, Raise);

        _store.Acquire(_folder);
        _watch = _store.Watch(_folder, _ => _changes.OnDocumentChanged(_folder));
    }

    /// <inheritdoc />
    public event EventHandler<DiagramDeltasEventArgs>? Changed;

    /// <inheritdoc />
    public IReadOnlyList<DiagramDelta> Baseline()
    {
        var elements = _changes.Deliver();
        return elements.Count > 0
            ? [new DiagramAddDelta(elements)]
            : [];
    }

    /// <inheritdoc />
    public IReadOnlyList<DiagramDelta> UpdateView(DiagramViewport viewport)
    {
        _viewport = viewport;

        // Diffed against what this connection holds, removals first (backend-centralization
        // R4.5) - and recorded, so a chart change next diffs against the narrowed set rather
        // than against the whole diagram last seen at baseline.
        return _changes.Refresh();
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
        _watch.Dispose();
        _store.Release(_folder);
        return ValueTask.CompletedTask;
    }

    /// <summary>What this connection should hold: the chart as the store has it, arranged, through its viewport.</summary>
    private IReadOnlyList<DiagramElement> Render()
    {
        var chart = _store.GetOrLoad(_folder);
        var graph = HelmGraph.Derive(chart);

        return _mapper.Visible(chart, graph, HelmLayout.Arranged(chart, graph, RegistrationLayout.Read(_registrationPath)), _viewport);
    }

    private void Raise(IReadOnlyList<DiagramDelta> deltas) =>
        Changed?.Invoke(this, new DiagramDeltasEventArgs(deltas));
}
