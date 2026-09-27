using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.GartnerHypeCycleGraph;

/// <summary>Sets the Description of a trend, a trigger or an influence. Empty removes it.</summary>
public sealed record SetGhgDescriptionCommand(string BodyPath, string Id, string Description) : ICommand;
