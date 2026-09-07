

using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.Timeline;

/// <summary>Changes what a connection is called; an empty label removes the key.</summary>
/// <param name="BodyPath">The document.</param>
/// <param name="ConnectionId">Which connection.</param>
/// <param name="Label">The new label, or empty for none.</param>
public sealed record RelabelTimelineConnectionCommand(
    string BodyPath,
    string ConnectionId,
    string Label) : ICommand;
