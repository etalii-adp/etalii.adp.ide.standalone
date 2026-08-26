using EtAlii.Adp.Backend;
using EtAlii.Adp.Backend.Diagrams;
using Serilog;

namespace EtAlii.Adp.Diagram.AzurePipeline;

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
/// underneath - arrives the same way: the store raises its event and the whole view is delivered
/// again as an Add. Adds are upserts, so re-delivering is a complete answer for anything that
/// still exists (Requirement 11.5). What no longer exists has to be said explicitly, which is why
/// this session remembers what it has already told the connection about.
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
    /// What this connection has been told about. Kept so a change can say what disappeared: a
    /// stage somebody deleted would otherwise stay on the canvas, because nothing mentions it.
    /// </summary>
    private HashSet<string> _delivered = new(StringComparer.Ordinal);

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
        _documents.Changed += OnDocumentChanged;
        _views.StageExpanded += OnStageExpanded;
    }

    public event EventHandler<DiagramDeltasEventArgs>? Changed;

    /// <summary>Why this pipeline cannot be shown, or empty when it can (Requirement 3.6).</summary>
    public string Unavailable => _documents.GetOrLoad(_rootPath, _bodyPath).Error;

    public IReadOnlyList<DiagramDelta> Baseline()
    {
        var elements = Deliver();
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
        return Difference();
    }

    /// <summary>Whether this connection has <paramref name="stageId"/> open.</summary>
    public bool IsExpanded(string stageId) => _views.For(_watchId, _bodyPath).IsExpanded(stageId);

    /// <summary>
    /// Pushes the jobs a stage has just revealed, or takes back the ones it hid
    /// (Requirements 8.2 and 8.6).
    /// </summary>
    /// <remarks>
    /// Only this connection's own toggles: which stages somebody has opened is a property of
    /// looking rather than of the pipeline, so nothing is written and no other viewer is told.
    /// </remarks>
    private void OnStageExpanded(object? sender, PipelineStageExpandedEventArgs args)
    {
        if (args.WatchId != _watchId ||
            !string.Equals(args.BodyPath, _bodyPath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var deltas = Difference();
        if (deltas.Count > 0)
        {
            Changed?.Invoke(this, new DiagramDeltasEventArgs(deltas));
        }

        _logger.Debug(
            "Watch {WatchId} {Action} {StageId}, pushing {Count} deltas",
            _watchId,
            args.Expanded ? "opened" : "closed",
            args.StageId,
            deltas.Count);
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
        _views.StageExpanded -= OnStageExpanded;
        _views.Forget(_watchId, _bodyPath);
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// The deltas carrying this connection from what it has to what it should have.
    /// </summary>
    /// <remarks>
    /// The "before" is what was actually delivered rather than a freshly computed view, so this
    /// stays correct even when the two differ - which is the case that matters, since it is the
    /// only one where a delta is doing any work.
    /// </remarks>
    private IReadOnlyList<DiagramDelta> Difference()
    {
        var before = _delivered;
        var after = Deliver();

        var appeared = after.Where(element => !before.Contains(element.Id)).ToArray();
        var removed = before.Except(after.Select(element => element.Id), StringComparer.Ordinal).ToArray();

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
    /// What this connection should be seeing, recorded as what it now has.
    /// </summary>
    /// <remarks>
    /// Everything that leaves this session goes through here, so there is one place that knows
    /// what the connection holds and no way to deliver elements without recording them.
    /// </remarks>
    private IReadOnlyList<DiagramElement> Deliver()
    {
        var entry = _documents.GetOrLoad(_rootPath, _bodyPath);
        // A file that does not parse has an empty model, so this delivers nothing and the diagram
        // shows as unavailable - the honest answer, and it keeps a half-read pipeline from being
        // drawn as though it were the whole one.
        var elements = entry.IsUsable ? _mapper.Visible(entry.Model, _viewport, _views.For(_watchId, _bodyPath).ExpandedStageIds) : [];
        _delivered = elements.Select(element => element.Id).ToHashSet(StringComparer.Ordinal);
        return elements;
    }

    private void OnDocumentChanged(object? sender, PipelineDocumentChangedEventArgs args)
    {
        if (!string.Equals(args.Path, _bodyPath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        // Everything is re-delivered rather than only what appeared. An edited element keeps its
        // id, so a difference would find nothing to add and the connection would never learn the
        // new state - and Requirement 11.5 says an edit *is* an Add carrying the element as it now
        // is. Adds are upserts, so re-sending what did not change costs a message and nothing else.
        var before = _delivered;
        var elements = Deliver();
        var gone = before.Except(elements.Select(element => element.Id), StringComparer.Ordinal).ToArray();

        var deltas = new List<DiagramDelta>();
        if (elements.Count > 0)
        {
            deltas.Add(new DiagramAddDelta(elements));
        }

        if (gone.Length > 0)
        {
            deltas.Add(new DiagramRemoveDelta(gone));
        }

        if (deltas.Count > 0)
        {
            Changed?.Invoke(this, new DiagramDeltasEventArgs(deltas));
        }

        _logger.Debug(
            "Pushed {Count} elements and {Removed} removals to watch {WatchId} after {Path} changed",
            elements.Count,
            gone.Length,
            _watchId,
            args.Path);
    }
}
