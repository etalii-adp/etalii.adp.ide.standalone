using Serilog;

namespace EtAlii.Adp.Diagram.AzureDevOpsPipeline;

/// <summary>
/// One open pipeline diagram for one connection: which file, and which of its stages this
/// connection has expanded.
/// </summary>
/// <remarks>
/// <para>
/// The document is shared and the expansion is not. Two people looking at one pipeline are looking
/// at the same stages and the same dependencies, but which of those stages each of them has opened
/// is a property of looking rather than of the pipeline - so it lives here, per connection, and
/// never touches the file. That is the same reasoning the mindmap module reached about fold state.
/// </para>
/// <para>
/// A change of any shape - an edit through this connection, an edit through another, a git pull
/// underneath, a stage opened or closed - arrives the same way: the view is rendered again and
/// diffed against what this connection was last given. What is new or changed is added, which is
/// how an edit reaches the canvas (Requirement 11.5), and what is gone is removed, since nothing
/// else would take it off. Both the diff and the reaction to the store's event are the shared ones
/// (backend-centralization R4 and R5), and <see cref="DiagramDocumentChangeHandler"/> keeps the
/// one record of what this connection holds.
/// </para>
/// </remarks>
public sealed class PipelineSession : IDiagramSession
{
    private static readonly ILogger _logger = Log.ForContext<PipelineSession>();

    private readonly ShortGuid _watchId;
    private readonly string _rootPath;
    private readonly string _bodyPath;
    private readonly IPipelineDocumentStore _documents;
    private readonly PipelineElementMapper _mapper;
    private readonly PipelineViewState _views;

    /// <summary>
    /// What this connection was last given, and the one way it is changed: a stage somebody
    /// deleted would otherwise stay on the canvas, because nothing mentions it again.
    /// </summary>
    private readonly DiagramDocumentChangeHandler _changes;

    private DiagramViewport _viewport = DiagramViewport.Unbounded;

    /// <summary>Opens a session on <paramref name="bodyPath"/> for one connection.</summary>
    public PipelineSession(
        ShortGuid watchId,
        string rootPath,
        string bodyPath,
        IPipelineDocumentStore documents,
        PipelineElementMapper mapper,
        PipelineViewState views)
    {
        ArgumentNullException.ThrowIfNull(views);
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(bodyPath);
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(mapper);

        _watchId = watchId;
        _rootPath = rootPath;
        _bodyPath = bodyPath;
        _documents = documents;
        _mapper = mapper;
        _views = views;
        _changes = new DiagramDocumentChangeHandler(
            bodyPath,
            Render,
            deltas => Changed?.Invoke(this, new DiagramDeltasEventArgs(deltas)));
        _documents.Changed += OnDocumentChanged;
        _views.ElementExpanded += OnElementExpanded;
    }

    public event EventHandler<DiagramDeltasEventArgs>? Changed;

    /// <summary>Why this pipeline cannot be shown, or empty when it can (Requirement 3.6).</summary>
    public string Unavailable => _documents.GetOrLoad(_rootPath, _bodyPath).Error;

    public IReadOnlyList<DiagramDelta> Baseline()
    {
        var elements = _changes.Deliver();
        return elements.Count == 0 ? [] : [new DiagramAddDelta(elements)];
    }

    /// <summary>
    /// Narrows or widens what this connection sees.
    /// </summary>
    /// <remarks>
    /// The viewport is recorded and everything is delivered regardless: a pipeline is a small
    /// graph and filtering has little to do, which Requirement 11.8 permits explicitly. Recording
    /// it anyway leaves the seam where filtering would go, and means the day it starts mattering
    /// is a change to one method rather than a new path through this class.
    /// </remarks>
    public IReadOnlyList<DiagramDelta> UpdateView(DiagramViewport viewport)
    {
        _viewport = viewport;
        return _changes.Refresh();
    }

    /// <summary>Whether this connection has <paramref name="stageId"/> open.</summary>
    public bool IsExpanded(string stageId) => _views.For(_watchId, _bodyPath).IsExpanded(stageId);

    /// <summary>
    /// Pushes what a stage or a job has just revealed - its jobs, or its steps - or takes back
    /// what it hid (Requirements 8.2 and 8.6).
    /// </summary>
    /// <remarks>
    /// Only this connection's own toggles: which stages somebody has opened is a property of
    /// looking rather than of the pipeline, so nothing is written and no other viewer is told.
    /// </remarks>
    private void OnElementExpanded(object? sender, PipelineElementExpandedEventArgs args)
    {
        if (args.WatchId != _watchId ||
            !string.Equals(args.BodyPath, _bodyPath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _logger.Debug(
            "Watch {WatchId} {Action} {ElementId}",
            _watchId,
            args.Expanded ? "opened" : "closed",
            args.ElementId);

        // A toggle is answered exactly as a change to this session's own document is: render,
        // diff against what was delivered, raise if there is anything. Going through the handler
        // rather than through a Refresh and a raise of its own keeps the raise under the handler's
        // lock, so a toggle's push and a document change's push cannot overtake one another.
        _changes.OnDocumentChanged(_bodyPath);
    }

    /// <summary>
    /// Refused: an Azure pipeline has no coordinates, so a drag would have nowhere to go.
    /// </summary>
    /// <remarks>
    /// The layout is computed from the dependency graph, which is what the file says. Moving a
    /// stage would therefore either be forgotten on the next open, or have to be written into the
    /// pipeline - and inventing a place to keep coordinates in an executable build definition is
    /// exactly the annotation Requirement 3.3 forbids. What a stage waits for is changed by
    /// editing <c>dependsOn</c>, which is a real edit with a real undo.
    /// </remarks>
    public Task<string> MoveElementAsync(string elementId, string newParentId, int index, CancellationToken cancellationToken)
    {
        _ = elementId;
        _ = newParentId;
        _ = index;
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(
            "A pipeline arranges itself from its dependencies. Change what a stage waits for to change where it sits.");
    }

    public ValueTask DisposeAsync()
    {
        _documents.Changed -= OnDocumentChanged;
        _views.ElementExpanded -= OnElementExpanded;
        _views.Forget(_watchId, _bodyPath);
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// What this connection should be seeing - every change is diffed against the last of these.
    /// </summary>
    private IReadOnlyList<DiagramElement> Render()
    {
        var entry = _documents.GetOrLoad(_rootPath, _bodyPath);
        // A file that does not parse has an empty model, so this delivers nothing and the diagram
        // shows as unavailable - the honest answer, and it keeps a half-read pipeline from being
        // drawn as though it were the whole one.
        return entry.IsUsable ? _mapper.Visible(entry.Model, _viewport, _views.For(_watchId, _bodyPath).ExpandedIds) : [];
    }

    private void OnDocumentChanged(object? sender, PipelineDocumentChangedEventArgs args) =>
        _changes.OnDocumentChanged(args.Path);
}
