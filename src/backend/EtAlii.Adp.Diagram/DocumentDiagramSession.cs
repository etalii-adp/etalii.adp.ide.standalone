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
/// <typeparam name="TBody">The type the store reads a body into, as its <see cref="IReadableDiagramDocumentStore{TBody, TModel}"/> says.</typeparam>
/// <typeparam name="TModel">What the module's parser reads from a body, and what it renders.</typeparam>
public abstract class DocumentDiagramSession<TBody, TModel> : IDiagramSession
{
    private readonly IReadableDiagramDocumentStore<TBody, TModel> _documents;
    private readonly IHistoryStack? _history;
    private readonly DiagramDocumentChangeHandler _changes;
    private DiagramViewport _viewport = DiagramViewport.Unbounded;

    /// <param name="bodyPath">The document this session shows.</param>
    /// <param name="documents">
    /// The store that holds it; the session follows its changes until disposed. Only its reading half,
    /// because the session writes nothing itself: an edit is a command, which takes the store it writes.
    /// </param>
    /// <param name="history">The project's history, or null for a session that may not edit.</param>
    protected DocumentDiagramSession(string bodyPath, IReadableDiagramDocumentStore<TBody, TModel> documents, IHistoryStack? history)
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

    /// <summary>What a drag answers when the session was opened without the project's history.</summary>
    protected virtual string ReadOnlyRefusal => "This diagram is read-only.";

    /// <inheritdoc />
    public IReadOnlyList<DiagramDelta> Baseline()
    {
        var elements = _changes.Deliver();
        var deltas = new List<DiagramDelta>();
        if (elements.Count > 0)
        {
            deltas.Add(new DiagramAddDelta(elements));
        }

        deltas.AddRange(Groups(Model()));
        return deltas;
    }

    /// <inheritdoc />
    public IReadOnlyList<DiagramDelta> UpdateView(DiagramViewport viewport)
    {
        _viewport = viewport;
        return _changes.Refresh();
    }

    /// <inheritdoc />
    public async Task<string> MoveElementAsync(string elementId, string newParentId, int index, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var move = ParentMoveOf(elementId, newParentId, index);
        if (move.Command is not { } command)
        {
            return move.Refusal;
        }

        if (_history is null)
        {
            return ReadOnlyRefusal;
        }

        var result = await _history.ExecuteAsync(command, cancellationToken);
        return result.IsSuccess ? "" : result.Error;
    }

    /// <inheritdoc />
    public async Task<string> MoveElementToAsync(string elementId, double x, double y, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(elementId);
        cancellationToken.ThrowIfCancellationRequested();

        if (_history is null)
        {
            return ReadOnlyRefusal;
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
        OnDisposing();
        return ValueTask.CompletedTask;
    }

    /// <summary>What the connection should hold of <paramref name="model"/> through <paramref name="viewport"/>.</summary>
    protected abstract IReadOnlyList<DiagramElement> Render(TModel model, DiagramViewport viewport);

    /// <summary>
    /// What the baseline sends after the elements: the groups a diagram draws around some of them,
    /// such as a Wardley map's pipelines. None by default.
    /// </summary>
    protected virtual IReadOnlyList<DiagramDelta> Groups(TModel model) => [];

    /// <summary>
    /// What a drop of <paramref name="elementId"/> with its top-left at (<paramref name="x"/>,
    /// <paramref name="y"/>) is: a command, nothing (dropped back where it was) or a refusal. By
    /// default the diagram cannot be arranged by dragging.
    /// </summary>
    protected virtual DiagramPlacement PlacementOf(TModel model, string elementId, double x, double y) =>
        DiagramPlacement.Refused("This diagram cannot be arranged by dragging.");

    /// <summary>
    /// What a drag of <paramref name="elementId"/> onto <paramref name="newParentId"/>, at
    /// <paramref name="index"/> among its children, is. By default a refusal in
    /// <see cref="ParentRefusal"/>'s words; a diagram whose elements form a tree answers with the
    /// command that moves one.
    /// </summary>
    protected virtual DiagramPlacement ParentMoveOf(string elementId, string newParentId, int index) =>
        DiagramPlacement.Refused(ParentRefusal);

    /// <summary>
    /// What the session shows of <paramref name="entry"/>: its model. A diagram whose files are
    /// written by other programs while open, and so are often half written, answers with what it
    /// last read while the entry cannot be read.
    /// </summary>
    protected virtual TModel Shown(DocumentEntry<TBody, TModel> entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return entry.Model;
    }

    /// <summary>The model the session shows now: the store's document at <see cref="BodyPath"/>, as <see cref="Shown"/> says.</summary>
    private TModel Model() => Shown(_documents.GetOrLoad(BodyPath));

    /// <summary>
    /// Renders again and raises what changed, as a change to the document does: for a session whose
    /// rendering also follows something besides its document, such as a selection.
    /// </summary>
    protected void Rerender() => _changes.OnDocumentChanged(BodyPath);

    /// <summary>Stops following whatever else the session subscribed to; the store it leaves itself.</summary>
    protected virtual void OnDisposing()
    {
    }

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
