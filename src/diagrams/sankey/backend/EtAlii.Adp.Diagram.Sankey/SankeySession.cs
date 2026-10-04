using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.Sankey;

/// <summary>One connection's view of one <c>.skv</c> document.</summary>
/// <remarks>
/// <para>
/// <b>What the connection holds is kept by <see cref="DiagramDocumentChangeHandler"/>, not here</b>,
/// so the baseline, a viewport report and a document change all diff under its one lock against
/// the one record of what was delivered.
/// </para>
/// <para>
/// <b>A drag is a reorder.</b> Nothing in a Sankey diagram has a position of its own, so a node
/// dropped higher or lower in its column takes its place among the others there - its entry moves
/// before the node it was dropped above - and the layout draws it there. Where across it was
/// dropped does not matter: a node's column is its flows'.
/// </para>
/// </remarks>
public sealed class SankeySession : IDiagramSession
{
    private readonly string _bodyPath;
    private readonly ISankeyDocumentStore _documents;
    private readonly SankeyElementMapper _mapper;
    private readonly IHistoryStack? _history;
    private readonly DiagramDocumentChangeHandler _changes;
    private DiagramViewport _viewport = DiagramViewport.Unbounded;

    public SankeySession(string bodyPath, ISankeyDocumentStore documents, SankeyElementMapper mapper, IHistoryStack? history = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bodyPath);
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(mapper);

        _bodyPath = bodyPath;
        _documents = documents;
        _mapper = mapper;
        _history = history;
        _changes = new DiagramDocumentChangeHandler(bodyPath, Render, Raise);
        _documents.Changed += OnDocumentChanged;
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
        return Task.FromResult("A node takes its place by being dragged up or down its column.");
    }

    /// <summary>
    /// Takes a node dropped with its top-left at (<paramref name="x"/>, <paramref name="y"/>) to its
    /// place in its column: above the first node whose middle is below the drop's middle.
    /// </summary>
    public async Task<string> MoveElementToAsync(string elementId, double x, double y, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(elementId);
        _ = x;
        cancellationToken.ThrowIfCancellationRequested();

        if (_history is null)
        {
            return "This diagram is read-only.";
        }

        var layout = SankeyLayout.Of(_documents.GetOrLoad(_bodyPath).Model);
        if (!layout.Boxes.TryGetValue(elementId, out var box))
        {
            return "That is not something this diagram can move.";
        }

        if (PlaceOf(layout, elementId, y + (box.Height / 2)) is not { } place)
        {
            // Dropped back where it was: nothing to write, and nothing to refuse.
            return "";
        }

        var result = await _history.ExecuteAsync(new MoveSankeyNodeCommand(_bodyPath, elementId, place.Anchor, place.After), cancellationToken);
        return result.IsSuccess ? "" : result.Error;
    }

    /// <summary>
    /// Where a node dropped with its middle at <paramref name="centreY"/> goes: before the first
    /// other node of its column whose middle is lower, or after the last one - or <c>null</c> when
    /// that is where it already is.
    /// </summary>
    internal static (string Anchor, bool After)? PlaceOf(SankeyLayout layout, string id, double centreY)
    {
        ArgumentNullException.ThrowIfNull(layout);

        var column = layout.ColumnOrder[layout.Columns[id]];
        var others = column.Where(other => other != id).ToList();
        if (others.Count == 0)
        {
            return null;
        }

        var index = others.Count(other => layout.Boxes[other].CentreY < centreY);
        if (index == column.ToList().IndexOf(id))
        {
            return null;
        }

        return index < others.Count ? (others[index], false) : (others[^1], true);
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        _documents.Changed -= OnDocumentChanged;
        return ValueTask.CompletedTask;
    }

    private IReadOnlyList<DiagramElement> Render() => _mapper.Visible(_documents.GetOrLoad(_bodyPath).Model, _viewport);

    private void Raise(IReadOnlyList<DiagramDelta> deltas) =>
        Changed?.Invoke(this, new DiagramDeltasEventArgs(deltas));

    private void OnDocumentChanged(object? sender, SankeyDocumentChangedEventArgs args) =>
        _changes.OnDocumentChanged(args.Path);
}
