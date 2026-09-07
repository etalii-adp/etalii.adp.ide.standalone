

using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.Timeline;

/// <summary>Removes one connection.</summary>
/// <param name="BodyPath">The document.</param>
/// <param name="ConnectionId">Which relation.</param>
/// <param name="RemoveEmptiedConnectionsSection">Whether an emptied <c>connections:</c> key goes too - set by an inverse undoing the insert that created it.</param>
public sealed record DisconnectTimelineConnectionCommand(
    string BodyPath,
    string ConnectionId,
    bool RemoveEmptiedConnectionsSection = false) : ICommand;
