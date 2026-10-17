using EtAlii.Adp.Diagram.Modules;
using EtAlii.Adp.Hierarchy;
using EtAlii.Adp.History;
using EtAlii.Adp.Layout;

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
internal sealed class CausalLoopSession : DocumentDiagramSession<CausalLoopBody, CausalLoopModel>
{
    private readonly string? _registrationPath;
    private readonly CausalLoopElementMapper _mapper;

    /// <summary>
    /// The project's history, so an arrangement and an added variable are one undo away. Null makes
    /// the diagram read-only. The base holds its own for a drag; these two gestures are this module's.
    /// </summary>
    private readonly IHistoryStack? _history;

    public CausalLoopSession(
        string bodyPath,
        string? registrationPath,
        ICausalLoopDocumentStore documents,
        CausalLoopElementMapper mapper,
        IHistoryStack? history = null)
        : base(bodyPath, documents, history)
    {
        ArgumentNullException.ThrowIfNull(mapper);

        _registrationPath = registrationPath;
        _mapper = mapper;
        _history = history;
    }

    /// <inheritdoc />
    protected override string ParentRefusal =>
        "A causal loop element has no parent to move it under; dragging changes where it sits on the canvas.";

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
            return ReadOnlyRefusal;
        }

        if (_registrationPath is not { Length: > 0 })
        {
            return "This diagram was opened without a registration, so there is nowhere to store a position.";
        }

        var result = await _history.ExecuteAsync(
            new AddVariableAtCommand(BodyPath, _registrationPath, id, label, x, y),
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
            return ReadOnlyRefusal;
        }

        if (_registrationPath is not { Length: > 0 })
        {
            return "This diagram was opened without a registration, so there is nowhere to store an arrangement. Register the file to arrange it.";
        }

        var result = await _history.ExecuteAsync(
            new ArrangeCausalLoopCommand(_registrationPath, BodyPath), cancellationToken);

        return result.IsSuccess ? "" : result.Error;
    }

    /// <summary>
    /// The whole document laid out, then filtered by the viewport - in that order, always.
    /// </summary>
    protected override IReadOnlyList<DiagramElement> Render(CausalLoopModel model, DiagramViewport viewport)
    {
        // Computed positions first; authored ones from the registration win element by element,
        // and a stored id that names nothing simply has nothing to override.
        var boxes = CausalLoopLayout.Compute(model);
        var stored = _registrationPath is { Length: > 0 }
            ? RegistrationLayout.Read(_registrationPath)
            : new Dictionary<string, RegistrationPosition>(StringComparer.Ordinal);

        if (stored.Count > 0)
        {
            var overlaid = new Dictionary<string, Rect>(boxes.Count, StringComparer.Ordinal);
            foreach ((string id, Rect box) in boxes)
            {
                // Positions are stored under the element id, which is what a drag reports.
                overlaid[id] = stored.TryGetValue($"variable:{id}", out var position)
                    ? box with { X = position.X - (box.Width / 2), Y = position.Y - (box.Height / 2) }
                    : box;
            }

            boxes = overlaid;
        }

        return _mapper.Visible(model, boxes, viewport);
    }

    /// <summary>
    /// Stores an authored position in the registration's <c>layout:</c> block, as one undoable
    /// command - and never writes the <c>.cld</c>.
    /// </summary>
    protected override DiagramPlacement PlacementOf(CausalLoopModel model, string elementId, double x, double y)
    {
        _ = model;

        if (_registrationPath is not { Length: > 0 })
        {
            return DiagramPlacement.Refused("This diagram was opened without a registration, so there is nowhere to store a position. Register the file to arrange it.");
        }

        // A link and a loop label are drawn where their members are, so neither has a position
        // of its own to store. Refused rather than silently accepted and lost.
        return elementId.StartsWith("variable:", StringComparison.Ordinal)
            ? DiagramPlacement.By(new SetRegistrationLayoutCommand(_registrationPath, elementId, x, y))
            : DiagramPlacement.Refused("Only a variable can be moved; a link is drawn between its ends and a loop label among its members.");
    }
}
