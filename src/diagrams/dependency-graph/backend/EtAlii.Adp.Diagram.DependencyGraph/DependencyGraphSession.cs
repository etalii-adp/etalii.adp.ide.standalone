using EtAlii.Adp.History;
using Serilog;

namespace EtAlii.Adp.Diagram.DependencyGraph;

/// <summary>
/// One open dependency graph for one connection. Core owns the stream, the authorization and the
/// connection lifetime; this owns what the graph <em>is</em> and expresses every change as a
/// <see cref="DiagramDelta"/>.
/// </summary>
/// <remarks>
/// <see cref="UpdateView"/> answers a reported viewport with the difference it makes: the nodes
/// it newly admits, then the ones it no longer does. The earlier reasoning here - that a bounded
/// graph of tens of nodes has nothing to virtualize - was a fair local judgement and is
/// superseded by view-delta-adoption, which asks the eleven modules for one loop rather than
/// eleven answers to the same question. A graph is bounded until someone points it at a
/// dependency tree that is not.
/// </remarks>
public sealed class DependencyGraphSession : IDiagramSession
{
    private static readonly ILogger _logger = Log.ForContext<DependencyGraphSession>();

    private readonly string _bodyPath;
    private readonly IDependencyGraphDocumentStore _documents;
    private readonly DependencyGraphElementMapper _mapper;

    /// <summary>The project's history, so a drag is one undo away. Null makes the graph read-only.</summary>
    private readonly IHistoryStack? _history;

    /// <summary>What this connection was last sent, so a change can be diffed against it.</summary>
    private IReadOnlyList<DiagramElement> _delivered = [];

    /// <summary>
    /// The last viewport this connection reported. Unbounded until it reports one, so a
    /// connection that never does keeps the whole graph it opened with.
    /// </summary>
    private DiagramViewport _viewport = DiagramViewport.Unbounded;

    /// <summary>Creates the session and subscribes to the store's changes.</summary>
    public DependencyGraphSession(
        string bodyPath,
        IDependencyGraphDocumentStore documents,
        DependencyGraphElementMapper mapper,
        IHistoryStack? history = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bodyPath);
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(mapper);

        _bodyPath = bodyPath;
        _documents = documents;
        _mapper = mapper;
        _history = history;
        _documents.Changed += OnDocumentChanged;
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

    /// <summary>
    /// What the reported viewport changes: an Add for the elements it newly admits, then a
    /// Remove for the ones that left. The order is the one both reference implementations use
    /// (view-delta-adoption Requirement 4.3, as the design corrected it).
    /// </summary>
    /// <remarks>
    /// The same render and the same diff a document change goes through, so the two paths cannot
    /// disagree about what this connection holds. Diffing against <c>_delivered</c> rather than
    /// against a recomputation of the old viewport is what keeps them honest: an edit arriving
    /// after a narrowing then re-sends only what actually changed, instead of a Remove for
    /// elements the client dropped when the view narrowed.
    /// </remarks>
    public IReadOnlyList<DiagramDelta> UpdateView(DiagramViewport viewport)
    {
        _viewport = viewport;

        var current = Render();
        var deltas = _mapper.Diff(_delivered, current);
        _delivered = current;

        return deltas;
    }

    /// <summary>
    /// Refused. A node has no parent a drag could change - its place is a coordinate and a row,
    /// which is <see cref="MoveElementToAsync"/>.
    /// </summary>
    public Task<string> MoveElementAsync(string elementId, string newParentId, int index, CancellationToken cancellationToken)
    {
        _ = elementId;
        _ = newParentId;
        _ = index;
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult("A node has no parent to move it under; dragging changes where it sits and which row it is on.");
    }

    /// <summary>
    /// Moves a node to a position. The point arrives in the module's own coordinate space -
    /// canvas units and row-height units - and is dispatched as one command so the drag is one
    /// undo away.
    /// </summary>
    public async Task<string> MoveElementToAsync(string elementId, double x, double y, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(elementId);
        cancellationToken.ThrowIfCancellationRequested();

        if (_history is null)
        {
            return "This graph is read-only.";
        }

        var entry = _documents.GetOrLoad(_bodyPath);
        var element = entry.Model.Elements.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, elementId, StringComparison.Ordinal));
        if (element is null)
        {
            // Also the answer for a relation's id: a curve has no position of its own - it
            // follows its endpoints.
            return "That element is not something this graph can move.";
        }

        var (movedX, row) = DependencyGraphElementMapper.Placement(x, y);
        var result = await _history.ExecuteAsync(
            new SetDependencyGraphPlacementCommand(_bodyPath, elementId, movedX, row, "Moved"),
            cancellationToken);

        return result.IsSuccess ? "" : result.Error;
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        _documents.Changed -= OnDocumentChanged;
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// What this connection should be holding: the elements the last reported viewport admits.
    /// Unbounded until one is reported, so an open delivers the whole graph exactly as before -
    /// and a save after a report re-delivers what is in view rather than undoing the cull.
    /// </summary>
    private IReadOnlyList<DiagramElement> Render() =>
        _mapper.Visible(_documents.GetOrLoad(_bodyPath).Model, _viewport);

    /// <summary>
    /// A save from any connection, or an edit made outside ADP, arrives here and goes out as
    /// the difference from what this connection was last sent.
    /// </summary>
    private void OnDocumentChanged(object? sender, DependencyGraphDocumentChangedEventArgs args)
    {
        if (!string.Equals(args.Path, _bodyPath, StringComparison.OrdinalIgnoreCase))
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
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A file that vanished or locked mid-reload costs this notification, never the
            // session: the next change tries again.
            _logger.Warning(exception, "Could not re-read {BodyPath} after a change", _bodyPath);
        }
    }
}
