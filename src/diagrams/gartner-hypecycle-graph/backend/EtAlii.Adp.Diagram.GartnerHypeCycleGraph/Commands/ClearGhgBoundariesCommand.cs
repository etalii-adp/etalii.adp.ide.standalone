using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.GartnerHypeCycleGraph;

/// <summary>Returns a trend to even phases by removing every dragged boundary (Requirement 3.5).</summary>
public sealed record ClearGhgBoundariesCommand(string BodyPath, string TrendId) : ICommand;
