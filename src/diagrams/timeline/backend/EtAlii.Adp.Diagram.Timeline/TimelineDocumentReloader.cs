using EtAlii.Adp.Common;
using EtAlii.Adp.Diagram;

namespace EtAlii.Adp.Diagram.Timeline;

/// <summary>
/// Core's reload seam for timelines: when the bridge sees a timeline body or its registration
/// change on disk, the store re-reads it and every open session hears (modular-text-editors
/// Requirement 5.3).
/// </summary>
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
}
