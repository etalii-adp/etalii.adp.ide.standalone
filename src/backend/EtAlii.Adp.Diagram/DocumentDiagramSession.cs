using EtAlii.Adp.Documents;
using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram;

/// <summary>
/// A session over one document of a store: what every such session did the same way, written once
/// (code-structure FR-031).
/// </summary>
/// <remarks>
/// <para>
/// <b>A module says what its diagram is, and nothing else.</b> It renders a model through a viewport
/// (<see cref="Render"/>) and, where its elements can be dragged, says which command a drop is
/// (<see cref="PlacementOf"/>). Following the store, delivering the baseline, answering a viewport
/// report, the read-only refusal and the dispatch through the project's history are here, where each
/// session used to write them again.
/// </para>
/// <para>
/// <b>What the connection holds is kept by <see cref="DiagramDocumentChangeHandler"/></b>, so the
/// baseline, a viewport report and a document change all diff under its one lock against the one
/// record of what was delivered.
/// </para>
/// </remarks>
/// <typeparam name="TBody">The type the store reads a body into, as its <see cref="IDiagramDocumentStore{TBody, TModel}"/> says.</typeparam>
/// <typeparam name="TModel">What the module's parser reads from a body, and what it renders.</typeparam>
public abstract class DocumentDiagramSession<TBody, TModel> : IDiagramSession
{
    private readonly IDiagramDocumentStore<TBody, TModel> _documents;
    private readonly IHistoryStack? _history;
    private readonly DiagramDocumentChangeHandler _changes;
    private DiagramViewport _viewport = DiagramViewport.Unbounded;

    /// <param name="bodyPath">The document this session shows.</param>
    /// <param name="documents">The store that holds it; the session follows its changes until disposed.</param>
    /// <param name="history">The project's history, or null for a session that may not edit.</param>
    protected DocumentDiagramSession(string bodyPath, IDiagramDocumentStore<TBody, TModel> documents, IHistoryStack? history)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bodyPath);
        ArgumentNullException.ThrowIfNull(documents);

        BodyPath = bodyPath;
        _documents = documents;
        _history = history;
        _changes = new DiagramDocumentChangeHandler(bodyPath, () => Render(Model(), _viewport), Raise);
        _documents.Changed += OnDocumentChanged;
    }

    /// <inheritdoc />
    public event EventHandler<DiagramDeltasEventArgs>? Changed;

    /// <summary>The document this session shows.</summary>
    protected string BodyPath { get; }

    /// <summary>What a drag onto another parent answers; no document diagram has one to change.</summary>
    protected virtual string ParentRefusal => "This diagram cannot be rearranged by giving an element another parent.";

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
        return Task.FromResult(ParentRefusal);
    }

    /// <inheritdoc />
    public async Task<string> MoveElementToAsync(string elementId, double x, double y, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(elementId);
        cancellationToken.ThrowIfCancellationRequested();

        if (_history is null)
        {
            return "This diagram is read-only.";
        }

        var placement = PlacementOf(Model(), elementId, x, y);
        if (placement.Command is not { } command)
        {
            return placement.Refusal;
        }

        var result = await _history.ExecuteAsync(command, cancellationToken);
        return result.IsSuccess ? "" : result.Error;
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        _documents.Changed -= OnDocumentChanged;
        return ValueTask.CompletedTask;
    }

    /// <summary>What the connection should hold of <paramref name="model"/> through <paramref name="viewport"/>.</summary>
    protected abstract IReadOnlyList<DiagramElement> Render(TModel model, DiagramViewport viewport);

    /// <summary>
    /// What a drop of <paramref name="elementId"/> with its top-left at (<paramref name="x"/>,
    /// <paramref name="y"/>) is: a command, nothing (dropped back where it was) or a refusal. By
    /// default the diagram cannot be arranged by dragging.
    /// </summary>
    protected virtual DiagramPlacement PlacementOf(TModel model, string elementId, double x, double y) =>
        DiagramPlacement.Refused("This diagram cannot be arranged by dragging.");

    /// <summary>The model the session shows now: the store's document at <see cref="BodyPath"/>.</summary>
    private TModel Model() => _documents.GetOrLoad(BodyPath).Model;

    private void Raise(IReadOnlyList<DiagramDelta> deltas) =>
        Changed?.Invoke(this, new DiagramDeltasEventArgs(deltas));

    private void OnDocumentChanged(object? sender, DocumentChangedEventArgs args) =>
        _changes.OnDocumentChanged(args.Path);
}

/// <summary>What a drop on the canvas is: a command to dispatch, nothing to do, or a refusal.</summary>
/// <param name="Command">The command that places the element, or null when there is none to dispatch.</param>
/// <param name="Refusal">Why the drop is refused; empty when it is not, or when there is nothing to do.</param>
public sealed record DiagramPlacement(ICommand? Command, string Refusal)
{
    /// <summary>The element was dropped where it already is: nothing to write, and nothing to refuse.</summary>
    public static DiagramPlacement Unchanged { get; } = new(null, "");

    /// <summary>The drop is placed by <paramref name="command"/>.</summary>
    public static DiagramPlacement By(ICommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        return new DiagramPlacement(command, "");
    }

    /// <summary>The drop is refused, for <paramref name="reason"/>.</summary>
    public static DiagramPlacement Refused(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        return new DiagramPlacement(null, reason);
    }
}
