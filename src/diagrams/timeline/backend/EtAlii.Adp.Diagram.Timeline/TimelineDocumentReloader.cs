using EtAlii.Adp.Documents;

namespace EtAlii.Adp.Diagram.Timeline;

/// <summary>
/// Core's reload seam for timelines: when the bridge sees a timeline body or its registration
/// change on disk, the store re-reads it and every open session hears (modular-text-editors
/// Requirement 5.3).
/// </summary>
/// <remarks>
/// <b><see cref="BodyDeleted"/> IS OVERRIDDEN, AND DROPPING THE OVERRIDE LOOKS HARMLESS AND IS NOT.</b>
/// <see cref="IDiagramDocumentReloader.BodyDeleted"/> defaults to a reload, which was right while a
/// read that failed installed an empty document. Since task 6 this store keeps the last good document
/// through a reload that cannot read, so a deletion routed to a reload would keep a deleted timeline on
/// the canvas for good, with nothing failing. The guard is <c>TimelineDocumentReloader.Tests</c>, seen
/// to fail with this override removed.
/// </remarks>
public sealed class TimelineDocumentReloader : IDiagramDocumentReloader
{
    private readonly ITimelineDocumentStore _documents;

    public TimelineDocumentReloader(ITimelineDocumentStore documents)
    {
        ArgumentNullException.ThrowIfNull(documents);
        _documents = documents;
    }

    public DiagramOrigin Origin => Diagram.Timeline.Origin;

    public void Reload(string rootPath, string bodyPath) => _documents.Reload(bodyPath);

    public void BodyDeleted(string rootPath, string bodyPath) => _documents.BodyDeleted(bodyPath);
}
