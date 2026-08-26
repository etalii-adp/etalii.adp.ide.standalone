using EtAlii.Adp.Backend;
using EtAlii.Adp.Backend.Diagrams;
using Serilog;

namespace EtAlii.Adp.Diagram.C4;

/// <summary>
/// One open C4 diagram for one connection: which document, which view within it, and what this
/// connection's viewport shows. Several sessions may share one document - that is the whole of
/// "model once, view many" - so an edit through any of them reaches all of them through the
/// store's change event (c4-diagrams Requirements 1.2, 1.5).
/// </summary>
public sealed class C4Session : IDiagramSession
{
    private static readonly ILogger _logger = Log.ForContext<C4Session>();

    private readonly ShortGuid _watchId;
    private readonly string _bodyPath;
    private readonly string? _viewKey;
    private readonly IC4DocumentStore _documents;
    private readonly C4ElementMapper _mapper;

    /// <summary>The project's history, so a drag is one undo away. Null makes the diagram read-only.</summary>
    private readonly IHistoryStack? _history;

    private DiagramViewport _viewport = DiagramViewport.Unbounded;

    public C4Session(
        ShortGuid watchId,
        string bodyPath,
        string? viewKey,
        IC4DocumentStore documents,
        C4ElementMapper mapper,
        IHistoryStack? history = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bodyPath);
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(mapper);

        _watchId = watchId;
        _bodyPath = bodyPath;
        _viewKey = viewKey;
        _documents = documents;
        _mapper = mapper;
        _history = history;
        _documents.Changed += OnDocumentChanged;
    }

    public event EventHandler<DiagramDeltasEventArgs>? Changed;

    public IReadOnlyList<DiagramDelta> Baseline()
    {
        var elements = Visible();
        return elements.Count == 0 ? [] : [new DiagramAddDelta(elements)];
    }

    public IReadOnlyList<DiagramDelta> UpdateView(DiagramViewport viewport)
    {
        var before = Visible().Select(element => element.Id).ToHashSet(StringComparer.Ordinal);
        _viewport = viewport;
        var after = Visible();

        var removed = before.Except(after.Select(element => element.Id), StringComparer.Ordinal).ToArray();
        var appeared = after.Where(element => !before.Contains(element.Id)).ToArray();

        var deltas = new List<DiagramDelta>();
        if (appeared.Length > 0)
        {
            deltas.Add(new DiagramAddDelta(appeared));
        }

        if (removed.Length > 0)
        {
            deltas.Add(new DiagramRemoveDelta(removed));
        }

        return deltas;
    }

    /// <summary>
    /// The drop half of a drag. On a C4 diagram a drag places an element on the canvas rather
    /// than re-parenting it: containment is what the model says, and dropping a container onto
    /// another system would be a claim about the architecture, not an arrangement. So the
    /// position is recorded and the containment is left alone (Requirement 8.3).
    /// </summary>
    /// <remarks>
    /// The core contract's <c>MoveElement</c> carries a parent and an index because a tree
    /// needs them. Where the canvas sends a position instead, it packs it into
    /// <paramref name="newParentId"/> as "x,y" - the one place the generic call shape and this
    /// type's meaning have to be reconciled.
    /// </remarks>
    /// <summary>
    /// Refused, always. A C4 element's parent is what the document declares - a container
    /// belongs to the software system it is written inside - so a drag on the canvas has no
    /// business changing it. Arranging is <see cref="MoveElementToAsync"/>.
    /// </summary>
    public Task<string> MoveElementAsync(string elementId, string newParentId, int index, CancellationToken cancellationToken)
    {
        _ = elementId;
        _ = newParentId;
        _ = index;
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult("Dragging changes where an element is drawn, not what contains it.");
    }

    /// <summary>
    /// Records where the user put an element, in the layout sidecar beside the document - a
    /// position is view state and the `.dsl` is another ecosystem's file (Requirement 3.5).
    /// </summary>
    public async Task<string> MoveElementToAsync(string elementId, double x, double y, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var view = View();
        if (view is null)
        {
            return "This diagram has no view to arrange.";
        }

        if (_history is null)
        {
            return "This diagram is read-only.";
        }

        var result = await _history.ExecuteAsync(
            new MoveC4ElementCommand(_bodyPath, view.Key, elementId, x, y),
            cancellationToken);

        return result.IsSuccess ? "" : result.Error;
    }

    public ValueTask DisposeAsync()
    {
        _documents.Changed -= OnDocumentChanged;
        return ValueTask.CompletedTask;
    }

    /// <summary>The view this session shows: the one its <c>.adp</c> named, or the document's first.</summary>
    public C4View? View()
    {
        var workspace = _documents.WorkspaceOf(_bodyPath);
        if (workspace.Views.Count == 0)
        {
            return null;
        }

        if (_viewKey is not { Length: > 0 })
        {
            // Requirement 2.6: a document opened without a registration shows its first view.
            return workspace.Views[0];
        }

        var named = workspace.FindView(_viewKey);
        if (named is not null)
        {
            return named;
        }

        _logger.Warning(
            "{BodyPath} declares no view '{ViewKey}'; it declares {Available}",
            _bodyPath,
            _viewKey,
            workspace.Views.Select(view => view.Key));
        return null;
    }

    private IReadOnlyList<DiagramElement> Visible()
    {
        var workspace = _documents.WorkspaceOf(_bodyPath);
        var view = View();
        return view is null ? [] : _mapper.Visible(workspace, view, _viewport, _bodyPath);
    }

    private void OnDocumentChanged(object? sender, C4DocumentChangedEventArgs args)
    {
        if (!string.Equals(args.Path, _bodyPath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        // Adds are upserts, so re-delivering the whole view is how a change of any shape - an
        // edit here, an edit through another view, an external save - reaches this connection.
        var elements = Visible();
        if (elements.Count > 0)
        {
            Changed?.Invoke(this, new DiagramDeltasEventArgs([new DiagramAddDelta(elements)]));
        }

        _logger.Debug("Pushed {Count} elements to watch {WatchId} after {Path} changed", elements.Count, _watchId, args.Path);
    }
}
