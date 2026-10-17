using EtAlii.Adp.Documents;
using EtAlii.Adp.Sessions;
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
/// else would take it off. Both are the session base's (code-structure FR-031): this session says
/// only what it draws and why it cannot be rearranged.
/// </para>
/// </remarks>
public sealed class PipelineSession : DocumentDiagramSession<LineDocument, PipelineModel>
{
    private static readonly ILogger _logger = Log.ForContext<PipelineSession>();

    private readonly ShortGuid _watchId;
    private readonly IDiagramDocumentStore<LineDocument, PipelineModel> _documents;
    private readonly PipelineElementMapper _mapper;
    private readonly PipelineViewState _views;

    /// <summary>Opens a session on <paramref name="bodyPath"/> for one connection.</summary>
    /// <remarks>
    /// The session base is handed the store as seen from <paramref name="rootPath"/>, because a
    /// pipeline's templates resolve against the root it was opened in. It is given no history: a
    /// pipeline has no gesture of its own on the canvas, only the edits its providers make.
    /// </remarks>
    public PipelineSession(
        ShortGuid watchId,
        string rootPath,
        string bodyPath,
        IPipelineDocumentStore documents,
        PipelineElementMapper mapper,
        PipelineViewState views)
        : this(watchId, bodyPath, In(documents, rootPath), mapper, views)
    {
    }

    private PipelineSession(
        ShortGuid watchId,
        string bodyPath,
        IDiagramDocumentStore<LineDocument, PipelineModel> documents,
        PipelineElementMapper mapper,
        PipelineViewState views)
        : base(bodyPath, documents, history: null)
    {
        ArgumentNullException.ThrowIfNull(mapper);
        ArgumentNullException.ThrowIfNull(views);

        _watchId = watchId;
        _documents = documents;
        _mapper = mapper;
        _views = views;
        _views.ElementExpanded += OnElementExpanded;
    }

    /// <summary>Why this pipeline cannot be shown, or empty when it can (Requirement 3.6).</summary>
    public string Unavailable => _documents.GetOrLoad(BodyPath).Unreadable;

    /// <summary>Whether this connection has <paramref name="stageId"/> open.</summary>
    public bool IsExpanded(string stageId) => _views.For(_watchId, BodyPath).IsExpanded(stageId);

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
    protected override string ParentRefusal =>
        "A pipeline arranges itself from its dependencies. Change what a stage waits for to change where it sits.";

    /// <summary>A drag is refused as it was before the session base: the session has no history, and nothing to place.</summary>
    protected override string ReadOnlyRefusal => "This diagram cannot be arranged by dragging.";

    /// <summary>
    /// What this connection should be seeing - every change is diffed against the last of these.
    /// </summary>
    /// <remarks>
    /// The viewport is passed on but filters little: a pipeline is a small graph, which
    /// Requirement 11.8 permits explicitly. A file that does not parse has an empty model, so it is
    /// drawn as nothing and the diagram shows as unavailable - the honest answer, and it keeps a
    /// half-read pipeline from being drawn as though it were the whole one.
    /// </remarks>
    protected override IReadOnlyList<DiagramElement> Render(PipelineModel model, DiagramViewport viewport) =>
        _mapper.Visible(model, viewport, _views.For(_watchId, BodyPath).ExpandedIds);

    protected override void OnDisposing()
    {
        _views.ElementExpanded -= OnElementExpanded;
        _views.Forget(_watchId, BodyPath);
    }

    /// <summary>
    /// Pushes what a stage or a job has just revealed - its jobs, or its steps - or takes back
    /// what it hid (Requirements 8.2 and 8.6).
    /// </summary>
    /// <remarks>
    /// Only this connection's own toggles: which stages somebody has opened is a property of
    /// looking rather than of the pipeline, so nothing is written and no other viewer is told.
    /// It is answered exactly as a change to the document is, so a toggle's push and a document
    /// change's push cannot overtake one another.
    /// </remarks>
    private void OnElementExpanded(object? sender, PipelineElementExpandedEventArgs args)
    {
        if (args.WatchId != _watchId ||
            !string.Equals(args.BodyPath, BodyPath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _logger.Debug(
            "Watch {WatchId} {Action} {ElementId}",
            _watchId,
            args.Expanded ? "opened" : "closed",
            args.ElementId);

        Rerender();
    }

    private static IDiagramDocumentStore<LineDocument, PipelineModel> In(IPipelineDocumentStore documents, string rootPath)
    {
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        return documents.In(rootPath);
    }
}
