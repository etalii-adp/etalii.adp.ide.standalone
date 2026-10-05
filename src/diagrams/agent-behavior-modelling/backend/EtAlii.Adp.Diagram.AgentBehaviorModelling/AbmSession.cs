using EtAlii.Adp.Hierarchy;
using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.AgentBehaviorModelling;

/// <summary>One connection's view of one behavior model.</summary>
/// <remarks>
/// <para>
/// <b>Two files feed what is drawn, and both are followed.</b> The Markdown holds the tree; the
/// registration's <c>layout:</c> block holds the positions an author dragged nodes to. A change to
/// the Markdown reaches this session through the store; a drag reaches it through the project's
/// history, because a position write goes through <see cref="ArrangeAbmNodeCommand"/>
/// and nothing watches the registration for it - the gap dotnet-dependency-graph found and closed
/// the same way.
/// </para>
/// <para>
/// <b>What the connection holds is kept in one place</b>: <see cref="DiagramDocumentChangeHandler"/>
/// renders, diffs and records under one lock for the baseline, the viewport and both kinds of change.
/// </para>
/// <para>
/// <b>A drag moves a row, and may reorder it.</b> The order of a node's children is the order they
/// run in, so where a box lands across decides its place among its siblings, and the Markdown is
/// rewritten to that order (<see cref="ArrangeAbmNodeCommand"/>). Where it lands down moves its
/// whole row, with everything beneath it; that height is kept in the registration.
/// </para>
/// </remarks>
public sealed class AbmSession : IDiagramSession
{
    private readonly string _bodyPath;
    private readonly string? _registrationPath;
    private readonly IAbmDocumentStore _documents;
    private readonly AbmElementMapper _mapper;
    private readonly IHistoryStack? _history;
    private readonly DiagramDocumentChangeHandler _changes;
    private DiagramViewport _viewport = DiagramViewport.Unbounded;

    public AbmSession(string bodyPath, string? registrationPath, IAbmDocumentStore documents, AbmElementMapper mapper, IHistoryStack? history = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bodyPath);
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(mapper);

        _bodyPath = bodyPath;
        _registrationPath = registrationPath;
        _documents = documents;
        _mapper = mapper;
        _history = history;
        _changes = new DiagramDocumentChangeHandler(bodyPath, Render, Raise);
        _documents.Changed += OnDocumentChanged;
        _history?.Changed += OnHistoryChanged;
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

    /// <summary>Moves a node under a new parent, at <paramref name="index"/> among its children.</summary>
    public async Task<string> MoveElementAsync(string elementId, string newParentId, int index, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(elementId);
        cancellationToken.ThrowIfCancellationRequested();

        if (_history is null)
        {
            return "This behavior model is read-only.";
        }

        var result = await _history.ExecuteAsync(new MoveAbmNodeCommand(_bodyPath, elementId, newParentId, index), cancellationToken);
        return result.IsSuccess ? "" : result.Error;
    }

    /// <summary>
    /// A node dropped with its top-left at (<paramref name="x"/>, <paramref name="y"/>): its row moves
    /// to that height, and it takes its place among its siblings by where it landed across.
    /// </summary>
    /// <remarks>A drop that changes neither is answered with nothing to refuse and nothing written.</remarks>
    public async Task<string> MoveElementToAsync(string elementId, double x, double y, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(elementId);
        cancellationToken.ThrowIfCancellationRequested();

        if (_history is null)
        {
            return "This behavior model is read-only.";
        }

        if (_registrationPath is not { Length: > 0 })
        {
            return "This behavior model was opened without a registration, so there is nowhere to keep a position.";
        }

        var model = _documents.GetOrLoad(_bodyPath).Model;
        if (model.NodeOf(elementId) is not { } node)
        {
            return "That is not something this behavior model can move.";
        }

        if (AbmArrangement.Of(model, Stored(), node, x, y).IsNothing)
        {
            return "";
        }

        var result = await _history.ExecuteAsync(new ArrangeAbmNodeCommand(_bodyPath, _registrationPath, elementId, x, y), cancellationToken);
        return result.IsSuccess ? "" : result.Error;
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        _documents.Changed -= OnDocumentChanged;

        // The history is the project's and outlives this session; an unremoved handler would keep a
        // closed diagram alive and re-rendering on every command anyone runs.
        _history?.Changed -= OnHistoryChanged;
        return ValueTask.CompletedTask;
    }

    private IReadOnlyList<DiagramElement> Render() =>
        _mapper.Visible(_documents.GetOrLoad(_bodyPath).Model, Stored(), _viewport);

    /// <summary>The positions the registration holds now; re-read per render, because the file is the truth.</summary>
    private IReadOnlyDictionary<string, RegistrationPosition> Stored() =>
        _registrationPath is { Length: > 0 }
            ? RegistrationLayout.Read(_registrationPath)
            : new Dictionary<string, RegistrationPosition>(StringComparer.Ordinal);

    private void Raise(IReadOnlyList<DiagramDelta> deltas) =>
        Changed?.Invoke(this, new DiagramDeltasEventArgs(deltas));

    private void OnDocumentChanged(object? sender, AbmDocumentChangedEventArgs args) =>
        _changes.OnDocumentChanged(args.Path);

    /// <summary>
    /// A command ran, was undone or redone, somewhere in the project: push whatever the stored
    /// positions now say. Another diagram's command costs a render and an empty diff.
    /// </summary>
    private void OnHistoryChanged(object? sender, EventArgs args)
    {
        IReadOnlyList<DiagramDelta> deltas;
        try
        {
            deltas = _changes.Refresh();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return; // the next change tries again
        }

        if (deltas.Count > 0)
        {
            Raise(deltas);
        }
    }
}
