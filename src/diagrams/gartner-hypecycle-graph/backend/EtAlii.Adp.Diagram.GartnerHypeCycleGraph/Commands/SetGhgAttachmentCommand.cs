using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.GartnerHypeCycleGraph;

/// <summary>Moves one end of an influence to another attachment point (Requirement 6.6).</summary>
/// <param name="BodyPath">The document.</param>
/// <param name="InfluenceId">The influence.</param>
/// <param name="Side"><c>from</c> or <c>to</c>.</param>
/// <param name="End">Where that end attaches now.</param>
public sealed record SetGhgAttachmentCommand(string BodyPath, string InfluenceId, string Side, GhgEnd End) : ICommand;
