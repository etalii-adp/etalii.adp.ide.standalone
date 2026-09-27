using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.GartnerHypeCycleGraph;

/// <summary>Adds a trigger where the toolbox's Trigger was dropped (Requirement 7.1).</summary>
/// <param name="BodyPath">The document.</param>
/// <param name="X">The drop's x. The trigger happens at the start of the step it falls in.</param>
/// <param name="Y">The drop's y. The trigger's centre lands on the middle of the nearest row.</param>
/// <param name="TriggerId">Empty to have one minted, once, as for <see cref="AddGhgTrendCommand"/>.</param>
public sealed record AddGhgTriggerCommand(string BodyPath, double X, double Y, string TriggerId = "") : ICommand;
