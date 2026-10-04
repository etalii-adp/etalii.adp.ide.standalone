using EtAlii.Adp.Documents;
using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.SupplyChain;

/// <summary>One connection's view of one <c>.supply</c> document.</summary>
/// <remarks>
/// <para>
/// <b>What the connection holds is kept by <see cref="DiagramDocumentChangeHandler"/>, not here</b>,
/// so the baseline, a viewport report and a change - document or selection - all diff under its one
/// lock against the one record of what was delivered.
/// </para>
/// <para>
/// <b>A selection change is a change.</b> When <see cref="SupplyChainSelections"/> says this
/// connection selected something else in this body, the session re-renders with the new trace and
/// the shared diff sends exactly the elements whose marking moved.
/// </para>
/// <para>
/// <b>A drag is a move through the stream.</b> A node is placed where it was dropped; a group is
/// moved by moving every member by the same distance, because its frame is theirs.
/// </para>
/// </remarks>
public sealed class SupplyChainSession : IDiagramSession
{
    private readonly ShortGuid _watchId;
    private readonly string _bodyPath;
    private readonly ISupplyChainDocumentStore _documents;
    private readonly SupplyChainElementMapper _mapper;
    private readonly SupplyChainSelections _selections;
    private readonly IHistoryStack? _history;
    private readonly DiagramDocumentChangeHandler _changes;
    private DiagramViewport _viewport = DiagramViewport.Unbounded;

    public SupplyChainSession(
        ShortGuid watchId,
        string bodyPath,
        ISupplyChainDocumentStore documents,
        SupplyChainElementMapper mapper,
        SupplyChainSelections selections,
        IHistoryStack? history = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bodyPath);
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(mapper);
        ArgumentNullException.ThrowIfNull(selections);

        _watchId = watchId;
        _bodyPath = bodyPath;
        _documents = documents;
        _mapper = mapper;
        _selections = selections;
        _history = history;
        _changes = new DiagramDocumentChangeHandler(bodyPath, Render, Raise);
        _documents.Changed += OnDocumentChanged;
        _selections.Changed += OnSelectionChanged;
    }

    /// <inheritdoc />
    public event EventHandler<DiagramDeltasEventArgs>? Changed;

    /// <inheritdoc />
    public IReadOnlyList<DiagramDelta> Baseline()
    {
        var elements = _changes.Deliver();
        return elements.Count > 0 ? [new DiagramAddDelta(elements)] : [];
    }

    /// <inheritdoc />
    public IReadOnlyList<DiagramDelta> UpdateView(DiagramViewport viewport)
    {
        _viewport = viewport;
        return _changes.Refresh();
    }

    /// <inheritdoc />
    public Task<string> MoveElementAsync(string elementId, string newParentId, int index, CancellationToken cancellationToken)
    {
        _ = elementId;
        _ = newParentId;
        _ = index;
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult("A node joins a group through its Group property, not by being dropped on it.");
    }

    /// <summary>Moves a node so its top-left is (<paramref name="x"/>, <paramref name="y"/>), or a group's frame, members and all.</summary>
    public async Task<string> MoveElementToAsync(string elementId, double x, double y, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(elementId);
        cancellationToken.ThrowIfCancellationRequested();

        if (_history is null)
        {
            return "This diagram is read-only.";
        }

        var layout = SupplyChainLayout.Of(_documents.GetOrLoad(_bodyPath).Model);
        Dictionary<string, (double X, double Y)> places = new(StringComparer.Ordinal);

        if (layout.NodeBoxes.ContainsKey(elementId))
        {
            places[elementId] = (x, y);
        }
        else if (layout.GroupBoxes.TryGetValue(elementId, out var frame))
        {
            var (dx, dy) = (x - frame.X, y - frame.Y);
            foreach (var member in layout.Nodes.Where(node => node.Group == elementId))
            {
                var box = layout.NodeBoxes[member.Id];
                places[member.Id] = (box.X + dx, box.Y + dy);
            }
        }
        else
        {
            return "That is not something this diagram can move.";
        }

        var result = await _history.ExecuteAsync(new PlaceSupplyChainNodesCommand(_bodyPath, places), cancellationToken);
        return result.IsSuccess ? "" : result.Error;
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        _documents.Changed -= OnDocumentChanged;
        _selections.Changed -= OnSelectionChanged;
        return ValueTask.CompletedTask;
    }

    private IReadOnlyList<DiagramElement> Render()
    {
        var model = _documents.GetOrLoad(_bodyPath).Model;
        var trace = SupplyChainTrace.Through(SupplyChainLayout.Of(model), _selections.Of(_watchId, _bodyPath));
        return _mapper.Visible(model, _viewport, trace);
    }

    private void Raise(IReadOnlyList<DiagramDelta> deltas) =>
        Changed?.Invoke(this, new DiagramDeltasEventArgs(deltas));

    private void OnDocumentChanged(object? sender, SupplyChainDocumentChangedEventArgs args) =>
        _changes.OnDocumentChanged(args.Path);

    private void OnSelectionChanged(object? sender, SupplyChainSelectionChangedEventArgs args)
    {
        if (args.WatchId != _watchId || !string.Equals(args.BodyPath, _bodyPath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        // Off the selecting thread: the context service raises this while it holds its own lock,
        // and a re-render writes to the delta stream, which is nothing to do while holding that.
        _ = Task.Run(() => _changes.OnDocumentChanged(_bodyPath));
    }
}
