using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.GartnerHypeCycleGraph;

/// <summary>
/// One connection's view of one <c>.ghg</c> document.
/// </summary>
/// <remarks>
/// <para>
/// <b>What the connection holds is kept in one place, and it is not here.</b>
/// <see cref="DiagramDocumentChangeHandler"/> owns the record of what was delivered, and this session
/// asks it for the baseline, for the answer to a viewport and for the reaction to a change. The
/// sessions this mirrors each kept their own copy and rendered, diffed and raised outside any lock,
/// so a change on the watcher's thread could interleave with a viewport report on a request's and
/// leave the client on a stale state. Holding no copy here is what makes the handler's one lock
/// cover all three.
/// </para>
/// <para>
/// <b>A drag is a move through the stream, not a context action</b> (the user's chat ruling of
/// 2026-09-25): the context channel carries no position, so the canvas sends the new top-left
/// through <see cref="MoveElementToAsync"/>, which runs <see cref="SetGhgPlacementCommand"/> on the
/// project's history - one undo away, like every other edit.
/// </para>
/// </remarks>
public sealed class GhgSession : IDiagramSession
{
    private readonly string _bodyPath;
    private readonly IGhgDocumentStore _documents;
    private readonly GhgElementMapper _mapper;
    private readonly IHistoryStack? _history;
    private readonly DiagramDocumentChangeHandler _changes;
    private DiagramViewport _viewport = DiagramViewport.Unbounded;

    public GhgSession(string bodyPath, IGhgDocumentStore documents, GhgElementMapper mapper, IHistoryStack? history = null)
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
        return Task.FromResult("A trend has no parent to move it under; it is placed in time and on a row.");
    }

    /// <summary>Moves a trend so its top-left is (<paramref name="x"/>, <paramref name="y"/>).</summary>
    /// <remarks>
    /// The canvas sends the top-left, not the centre it drew from, as FDG's does. The command snaps the
    /// left edge to a month and the top to a row, and shifts the trend's dates and stored boundaries by
    /// the months it moved (Requirement 3.5). An influence's id is refused - a curve follows its ends
    /// and has no position of its own.
    /// </remarks>
    public async Task<string> MoveElementToAsync(string elementId, double x, double y, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(elementId);
        cancellationToken.ThrowIfCancellationRequested();

        if (_history is null)
        {
            return "This graph is read-only.";
        }

        var model = _documents.GetOrLoad(_bodyPath).Model;
        if (GhgEdits.TrendOf(model, elementId) is null)
        {
            return "That is not something this graph can move.";
        }

        var result = await _history.ExecuteAsync(new SetGhgPlacementCommand(_bodyPath, elementId, x, y), cancellationToken);
        return result.IsSuccess ? "" : result.Error;
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        _documents.Changed -= OnDocumentChanged;
        return ValueTask.CompletedTask;
    }

    private IReadOnlyList<DiagramElement> Render() =>
        _mapper.Visible(_documents.GetOrLoad(_bodyPath).Model, _viewport);

    private void Raise(IReadOnlyList<DiagramDelta> deltas) =>
        Changed?.Invoke(this, new DiagramDeltasEventArgs(deltas));

    private void OnDocumentChanged(object? sender, GhgDocumentChangedEventArgs args) =>
        _changes.OnDocumentChanged(args.Path);
}
