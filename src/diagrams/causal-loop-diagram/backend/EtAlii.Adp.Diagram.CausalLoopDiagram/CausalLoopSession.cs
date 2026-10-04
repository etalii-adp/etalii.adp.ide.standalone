using EtAlii.Adp.Hierarchy;
using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.CausalLoopDiagram;

/// <summary>
/// One open causal loop diagram for one connection. Core owns the stream, the authorization and
/// the connection lifetime; this owns the document-to-elements reading and expresses every change
/// as a <see cref="DiagramDelta"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>The whole document is laid out, and the viewport filters the result.</b> Never the other way
/// round: laying out only what is visible makes the arrangement depend on where the reader
/// happens to be looking, so the diagram crawls under them as they pan (Requirement 10.3).
/// </para>
/// <para>
/// <b>What is new or changed is added, what is gone is removed - removals first.</b> Both a view
/// change and a document change are diffed against what this connection was last given, with the
/// one diff every module shares, whose order is one order for all of them (backend-centralization
/// R4.5): removing first can never delete something the same batch just added.
/// </para>
/// <para>
/// <b>A document change re-filters through the same viewport.</b> A change handler that rendered
/// unfiltered would re-send everything the viewport had just culled, which is invisible until
/// somebody edits a document while zoomed in.
/// </para>
/// <para>
/// <b>Only a read failure is caught.</b> The reaction to a document change is the shared one
/// (backend-centralization R5), which logs a file that vanished or locked mid-reload as a warning
/// naming the path and raises nothing. Anything else - a defect in the layout or the mapper -
/// propagates out of the store's change event to whoever raised it: for a change on disk that is
/// the reload bridge, whose folder watcher guards every event it dispatches and logs the failure
/// as an error naming the path. Until R5.2 this session caught every failure and logged it as its
/// own error, which hid a defect behind the same kind of line as a locked file.
/// </para>
/// </remarks>
internal sealed class CausalLoopSession : IDiagramSession
{
    private readonly string _bodyPath;
    private readonly string? _registrationPath;
    private readonly ICausalLoopDocumentStore _documents;
    private readonly CausalLoopElementMapper _mapper;

    /// <summary>The project's history, so an arrangement is one undo away. Null makes the diagram read-only.</summary>
    private readonly IHistoryStack? _history;

    /// <summary>What this connection was last sent, and the one way it is changed.</summary>
    private readonly DiagramDocumentChangeHandler _changes;

    /// <summary>
    /// The last viewport this connection reported. Unbounded until it reports one, so a client
    /// that never does - a test, or a canvas mid-load - still receives the whole diagram.
    /// </summary>
    private DiagramViewport _viewport = DiagramViewport.Unbounded;

    public CausalLoopSession(
        string bodyPath,
        string? registrationPath,
        ICausalLoopDocumentStore documents,
        CausalLoopElementMapper mapper,
        IHistoryStack? history = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bodyPath);
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(mapper);

        _bodyPath = bodyPath;
        _registrationPath = registrationPath;
        _documents = documents;
        _mapper = mapper;
        _history = history;

        // Rendered through the same viewport the connection last reported, whatever the cause.
        _changes = new DiagramDocumentChangeHandler(
            bodyPath,
            Render,
            deltas => Changed?.Invoke(this, new DiagramDeltasEventArgs(deltas)));
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

    /// <summary>Refused: nothing in this diagram nests under a parent - its place is a position.</summary>
    public Task<string> MoveElementAsync(string elementId, string newParentId, int index, CancellationToken cancellationToken)
    {
        _ = elementId;
        _ = newParentId;
        _ = index;
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult("A causal loop element has no parent to move it under; dragging changes where it sits on the canvas.");
    }

    /// <summary>
    /// Stores an authored position in the registration's <c>layout:</c> block, as one undoable
    /// command - and never writes the <c>.cld</c>.
    /// </summary>
    public async Task<string> MoveElementToAsync(string elementId, double x, double y, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(elementId);
        cancellationToken.ThrowIfCancellationRequested();

        if (_history is null)
        {
            return "This diagram is read-only.";
        }

        if (_registrationPath is not { Length: > 0 })
        {
            return "This diagram was opened without a registration, so there is nowhere to store a position. Register the file to arrange it.";
        }

        // A link and a loop label are drawn where their members are, so neither has a position
        // of its own to store. Refused rather than silently accepted and lost.
        if (!elementId.StartsWith("variable:", StringComparison.Ordinal))
        {
            return "Only a variable can be moved; a link is drawn between its ends and a loop label among its members.";
        }

        var result = await _history.ExecuteAsync(
            new SetRegistrationLayoutCommand(_registrationPath, elementId, x, y),
            cancellationToken);

        return result.IsSuccess ? "" : result.Error;
    }

    /// <summary>
    /// Declares a variable and authors its position at the point it was added, as one undoable
    /// step - the drop and the right-click "add variable" both land here. Where the diagram has no
    /// registration, there is nowhere to store a position, so the caller adds the variable
    /// without one and the layout places it.
    /// </summary>
    public async Task<string> AddVariableAtAsync(string id, string label, double x, double y, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        cancellationToken.ThrowIfCancellationRequested();

        if (_history is null)
        {
            return "This diagram is read-only.";
        }

        if (_registrationPath is not { Length: > 0 })
        {
            return "This diagram was opened without a registration, so there is nowhere to store a position.";
        }

        var result = await _history.ExecuteAsync(
            new AddVariableAtCommand(_bodyPath, _registrationPath, id, label, x, y),
            cancellationToken);

        return result.IsSuccess ? "" : result.Error;
    }

    /// <summary>
    /// Arranges the whole diagram with the self-organizing layout and stores the result as
    /// authored positions (causal-loop-diagram Requirement 6.1 and 6.8).
    /// </summary>
    /// <remarks>
    /// A user invokes this; nothing calls it when a document opens. The refusals are the two the
    /// requirement names - a diagram past the drawn-element budget, and one the separation pass
    /// could not resolve - and both arrive as the layout's own sentence rather than reworded
    /// here, so the same failure reads the same way wherever a user meets it.
    /// </remarks>
    public async Task<string> ArrangeAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_history is null)
        {
            return "This diagram is read-only.";
        }

        if (_registrationPath is not { Length: > 0 })
        {
            return "This diagram was opened without a registration, so there is nowhere to store an arrangement. Register the file to arrange it.";
        }

        var result = await _history.ExecuteAsync(
            new ArrangeCausalLoopCommand(_registrationPath, _bodyPath), cancellationToken);

        return result.IsSuccess ? "" : result.Error;
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        _documents.Changed -= OnDocumentChanged;
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// The whole document laid out, then filtered by the viewport - in that order, always.
    /// </summary>
    private IReadOnlyList<DiagramElement> Render()
    {
        var entry = _documents.GetOrLoad(_bodyPath);
        if (!entry.IsUsable)
        {
            return [];
        }

        // Computed positions first; authored ones from the registration win element by element,
        // and a stored id that names nothing simply has nothing to override.
        var boxes = CausalLoopLayout.Compute(entry.Model);
        var stored = _registrationPath is { Length: > 0 }
            ? RegistrationLayout.Read(_registrationPath)
            : new Dictionary<string, RegistrationPosition>(StringComparer.Ordinal);

        if (stored.Count > 0)
        {
            var overlaid = new Dictionary<string, CausalLoopBox>(boxes.Count, StringComparer.Ordinal);
            foreach ((string id, CausalLoopBox box) in boxes)
            {
                // Positions are stored under the element id, which is what a drag reports.
                overlaid[id] = stored.TryGetValue($"variable:{id}", out var position)
                    ? box with { X = position.X - (box.Width / 2), Y = position.Y - (box.Height / 2) }
                    : box;
            }

            boxes = overlaid;
        }

        return _mapper.Visible(entry.Model, boxes, _viewport);
    }

    private void OnDocumentChanged(object? sender, CausalLoopDocumentChangedEventArgs args) =>
        _changes.OnDocumentChanged(args.Path);
}
