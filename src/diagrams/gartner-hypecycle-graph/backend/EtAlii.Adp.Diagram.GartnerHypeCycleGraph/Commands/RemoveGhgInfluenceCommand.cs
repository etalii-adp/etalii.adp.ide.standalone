using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.GartnerHypeCycleGraph;

/// <summary>Removes one influence.</summary>
public sealed record RemoveGhgInfluenceCommand(string BodyPath, string InfluenceId) : ICommand;
