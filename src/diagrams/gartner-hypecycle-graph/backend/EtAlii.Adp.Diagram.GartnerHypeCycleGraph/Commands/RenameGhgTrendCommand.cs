using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.GartnerHypeCycleGraph;

/// <summary>Renames a trend, from the inline editor or the grid - one command for both (Requirement 5.2).</summary>
public sealed record RenameGhgTrendCommand(string BodyPath, string TrendId, string Name) : ICommand;
