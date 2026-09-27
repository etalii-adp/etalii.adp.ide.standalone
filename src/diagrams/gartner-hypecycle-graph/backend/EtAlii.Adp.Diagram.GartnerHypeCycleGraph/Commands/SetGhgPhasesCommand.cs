using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.GartnerHypeCycleGraph;

/// <summary>Sets how many phases a trend shows, 1 to 4 (Requirement 7.4).</summary>
/// <remarks>
/// Influences attached to a phase that becomes hidden are kept in the document unchanged; the canvas
/// hides them (Requirement 7.2). Stored boundaries are kept too, and apply again when their phase is
/// shown (Requirement 3.4).
/// </remarks>
public sealed record SetGhgPhasesCommand(string BodyPath, string TrendId, int Phases) : ICommand;
