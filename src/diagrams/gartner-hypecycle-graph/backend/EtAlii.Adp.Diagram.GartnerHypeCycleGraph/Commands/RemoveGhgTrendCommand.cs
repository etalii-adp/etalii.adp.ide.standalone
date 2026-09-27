using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.GartnerHypeCycleGraph;

/// <summary>Removes a trend and every influence touching it, in one edit so one undo restores them all.</summary>
public sealed record RemoveGhgTrendCommand(string BodyPath, string TrendId) : ICommand;
