using EtAlii.Adp.Documents;
using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.AgentActivityDiagram;

/// <summary>One connection's view of one activity file: its baseline, and every change after it as deltas.</summary>
public sealed class AadSession : IDiagramSession
{
    private readonly string _bodyPath;
    private readonly IAadDocumentStore _documents;
    private readonly AadElementMapper _mapper;
    private readonly IHistoryStack? _history;
    private readonly DiagramDocumentChangeHandler _changes;
    private DiagramViewport _viewport = DiagramViewport.Unbounded;
    private AadModel? _lastRead;

    public AadSession(string bodyPath, IAadDocumentStore documents, AadElementMapper mapper, IHistoryStack? history = null)
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

    public event EventHandler<DiagramDeltasEventArgs>? Changed;

    public IReadOnlyList<DiagramDelta> Baseline()
    {
        var elements = _changes.Deliver();
        return elements.Count > 0 ? [new DiagramAddDelta(elements)] : [];
    }

    public IReadOnlyList<DiagramDelta> UpdateView(DiagramViewport viewport)
    {
        _viewport = viewport;
        return _changes.Refresh();
    }

    public Task<string> MoveElementAsync(string elementId, string newParentId, int index, CancellationToken cancellationToken)
    {
        _ = elementId;
        _ = newParentId;
        _ = index;
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult("An element of this diagram has no parent to move it under; it is locked where it is dropped.");
    }

    /// <summary>
    /// A drag: the element is locked where it was let go (Requirement 6.2), through the project's
    /// history, so the lock is one undo away like every other edit.
    /// </summary>
    public async Task<string> MoveElementToAsync(string elementId, double x, double y, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(elementId);
        cancellationToken.ThrowIfCancellationRequested();

        if (_history is null)
        {
            return "This diagram is read-only.";
        }

        var result = await _history.ExecuteAsync(new PinAadElementCommand(_bodyPath, elementId, x, y), cancellationToken);
        return result.IsSuccess ? "" : result.Error;
    }

    public ValueTask DisposeAsync()
    {
        _documents.Changed -= OnDocumentChanged;
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// What the file says now. A file that cannot be read says nothing, so the diagram keeps showing
    /// what it last read until the file can be read again (Requirement 8.5).
    /// </summary>
    private IReadOnlyList<DiagramElement> Render()
    {
        var entry = _documents.GetOrLoad(_bodyPath);
        if (entry.IsUsable || _lastRead is null)
        {
            _lastRead = entry.Model;
        }

        return _mapper.Visible(_lastRead, _viewport);
    }

    private void Raise(IReadOnlyList<DiagramDelta> deltas) =>
        Changed?.Invoke(this, new DiagramDeltasEventArgs(deltas));

    private void OnDocumentChanged(object? sender, AadDocumentChangedEventArgs args) =>
        _changes.OnDocumentChanged(args.Path);
}

/// <summary>Opens an <see cref="AadSession"/> for a connection.</summary>
public sealed class AadSessionFactory : IDiagramSessionFactory
{
    private readonly IAadDocumentStore _documents;
    private readonly AadElementMapper _mapper;
    private readonly IHistoryStackStore _historyStacks;

    public AadSessionFactory(IAadDocumentStore documents, AadElementMapper mapper, IHistoryStackStore historyStacks)
    {
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(mapper);
        ArgumentNullException.ThrowIfNull(historyStacks);

        _documents = documents;
        _mapper = mapper;
        _historyStacks = historyStacks;
    }

    public DiagramOrigin Origin => Diagram.AgentActivity.Origin;

    public IDiagramSession Open(ShortGuid watchId, string rootPath, string bodyPath, string? registrationPath)
    {
        _ = watchId;
        _ = registrationPath;

        // The project's history, so a drag is one undo away like every other edit.
        return new AadSession(bodyPath, _documents, _mapper, _historyStacks.Get(rootPath));
    }
}
