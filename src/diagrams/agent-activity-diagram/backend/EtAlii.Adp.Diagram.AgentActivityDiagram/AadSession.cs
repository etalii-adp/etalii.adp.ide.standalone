using EtAlii.Adp.Documents;

namespace EtAlii.Adp.Diagram.AgentActivityDiagram;

/// <summary>One connection's view of one activity file: its baseline, and every change after it as deltas.</summary>
public sealed class AadSession : IDiagramSession
{
    private readonly string _bodyPath;
    private readonly IAadDocumentStore _documents;
    private readonly AadElementMapper _mapper;
    private readonly DiagramDocumentChangeHandler _changes;
    private DiagramViewport _viewport = DiagramViewport.Unbounded;

    public AadSession(string bodyPath, IAadDocumentStore documents, AadElementMapper mapper)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bodyPath);
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(mapper);

        _bodyPath = bodyPath;
        _documents = documents;
        _mapper = mapper;
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

    public ValueTask DisposeAsync()
    {
        _documents.Changed -= OnDocumentChanged;
        return ValueTask.CompletedTask;
    }

    private IReadOnlyList<DiagramElement> Render() =>
        _mapper.Visible(_documents.GetOrLoad(_bodyPath).Model, _viewport);

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

    public AadSessionFactory(IAadDocumentStore documents, AadElementMapper mapper)
    {
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(mapper);

        _documents = documents;
        _mapper = mapper;
    }

    public DiagramOrigin Origin => Diagram.AgentActivity.Origin;

    public IDiagramSession Open(ShortGuid watchId, string rootPath, string bodyPath, string? registrationPath)
    {
        _ = watchId;
        _ = rootPath;
        _ = registrationPath;
        return new AadSession(bodyPath, _documents, _mapper);
    }
}
