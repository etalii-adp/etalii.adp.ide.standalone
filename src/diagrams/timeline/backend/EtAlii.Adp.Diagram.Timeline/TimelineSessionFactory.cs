using EtAlii.Adp.Documents;
using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.Timeline;

/// <summary>
/// Opens a <see cref="TimelineSession"/> for one timeline.
/// </summary>
public sealed class TimelineSessionFactory : IDiagramSessionFactory
{
    private readonly ITimelineDocumentStore _documents;
    private readonly TimelineElementMapper _mapper;
    private readonly IHistoryStackStore _historyStacks;

    /// <summary>Creates the factory for the origin this module declares.</summary>
    public TimelineSessionFactory(
        ITimelineDocumentStore documents,
        TimelineElementMapper mapper,
        IHistoryStackStore historyStacks)
    {
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(mapper);
        ArgumentNullException.ThrowIfNull(historyStacks);

        _documents = documents;
        _mapper = mapper;
        _historyStacks = historyStacks;
    }

    /// <inheritdoc />
    public DiagramOrigin Origin => Diagram.Timeline.Origin;

    /// <summary>
    /// A session for the timeline at <paramref name="bodyPath"/>. The registration file is
    /// ignored: a `generic/timeline` `.adp` carries nothing beyond its MIME line, and a bare
    /// `.tml` routes here with no registration at all (Requirement 1.2).
    /// </summary>
    public IDiagramSession Open(ShortGuid watchId, string rootPath, string bodyPath, string? registrationPath)
    {
        _ = watchId;
        _ = registrationPath;

        // The project's history, so a drag is one undo away like every other edit.
        return new TimelineSession(bodyPath, _documents, _mapper, _historyStacks.Get(rootPath));
    }
}
