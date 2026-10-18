using EtAlii.Adp.Sessions;

namespace EtAlii.Adp.Workspace;

/// <summary>
/// A diagram session that <see cref="DiagramPump.Open"/> resolved and opened, not yet pumping: the
/// half of an open that can refuse, separated from the half that runs for the stream's life,
/// so a caller can answer the refusal before it commits to a stream.
/// </summary>
public sealed record OpenedDiagram(IDiagramSession Session, string BodyPath, ShortGuid WatchId);
