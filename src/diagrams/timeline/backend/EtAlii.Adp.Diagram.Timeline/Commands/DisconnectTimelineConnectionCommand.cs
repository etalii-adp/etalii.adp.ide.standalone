using EtAlii.Adp.Backend;

namespace EtAlii.Adp.Diagram.Timeline;

/// <summary>Removes one connection.</summary>
/// <param name="BodyPath">The document.</param>
/// <param name="ConnectionId">Which connection.</param>
public sealed record DisconnectTimelineConnectionCommand(
    string BodyPath,
    string ConnectionId) : ICommand;
