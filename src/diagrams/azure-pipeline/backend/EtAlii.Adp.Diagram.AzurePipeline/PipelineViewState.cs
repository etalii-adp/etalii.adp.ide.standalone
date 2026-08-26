using System.Collections.Concurrent;

namespace EtAlii.Adp.Diagram.AzurePipeline;

/// <summary>
/// What each connection has open on each pipeline, held per <c>(watchId, bodyPath)</c>.
/// </summary>
/// <remarks>
/// <para>
/// This exists because the two halves of a fold never meet otherwise. The <b>action provider</b>
/// is what a user reaches - it is handed a target and asked to do something - and the
/// <b>session</b> is what owns the stream a delta has to go out on. Neither can see the other, so
/// the state they share lives here and announces its own changes.
/// </para>
/// <para>
/// Putting the expansion inside the session instead is the mistake this replaces: it was correct
/// about ownership - expansion is per connection - and left nothing able to change it, so no stage
/// was ever opened and a pipeline's jobs were unreachable however hard anyone clicked. The mindmap
/// had already met and solved this, in <c>MindmapViewState</c>.
/// </para>
/// </remarks>
public sealed class PipelineViewState
{
    private readonly ConcurrentDictionary<(ShortGuid WatchId, string BodyPath), PipelineConnectionView> _views = new();

    /// <summary>
    /// Raised when a connection opens or closes a stage, so the session holding that connection's
    /// stream can push the matching deltas.
    /// </summary>
    /// <remarks>
    /// The only route a change is allowed to take: state and notification stay together, so a
    /// session cannot miss one the way it would if callers toggled a view directly.
    /// </remarks>
    public event EventHandler<PipelineStageExpandedEventArgs>? StageExpanded;

    /// <summary>The view for a connection on a pipeline, created on first ask.</summary>
    /// <remarks>
    /// A new one has nothing open: a pipeline read at three levels at once is unreadable, so the
    /// stage graph - the thing the diagram is for - is what a reader meets first (Requirement 8.2).
    /// </remarks>
    public PipelineConnectionView For(ShortGuid watchId, string bodyPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bodyPath);
        return _views.GetOrAdd((watchId, bodyPath.ToUpperInvariant()), _ => new PipelineConnectionView());
    }

    /// <summary>Opens a closed stage or closes an open one, and announces it.</summary>
    public bool Toggle(ShortGuid watchId, string bodyPath, string stageId)
    {
        var expanded = For(watchId, bodyPath).Toggle(stageId);
        StageExpanded?.Invoke(this, new PipelineStageExpandedEventArgs(watchId, bodyPath, stageId, expanded));
        return expanded;
    }

    /// <summary>Forgets what a connection had open, when its session goes away.</summary>
    public void Forget(ShortGuid watchId, string bodyPath) =>
        _views.TryRemove((watchId, bodyPath.ToUpperInvariant()), out _);
}
