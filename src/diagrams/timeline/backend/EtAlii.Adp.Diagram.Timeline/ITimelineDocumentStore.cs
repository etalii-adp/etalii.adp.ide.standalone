using EtAlii.Adp.Documents;

namespace EtAlii.Adp.Diagram.Timeline;

/// <summary>
/// The one owner of timeline documents on disk: one per open timeline, keyed by path, so two
/// diagrams on the same file share it and an edit made through one is visible in the other.
/// </summary>
/// <remarks>
/// <para>
/// <b>A marker.</b> Everything a store does is declared by <see cref="IDiagramDocumentStore{TBody, TModel}"/>;
/// this names the timeline's, so a registration and a constructor say which store they mean.
/// </para>
/// <para>
/// No workspace root in any signature, deliberately. The pipeline store carries one because a
/// pipeline's templates resolve relative to it; a timeline is one file and reads nothing else,
/// so a root would be a parameter every caller supplies and nothing uses.
/// </para>
/// </remarks>
public interface ITimelineDocumentStore : IDiagramDocumentStore<LineDocument, TimelineModel>;
