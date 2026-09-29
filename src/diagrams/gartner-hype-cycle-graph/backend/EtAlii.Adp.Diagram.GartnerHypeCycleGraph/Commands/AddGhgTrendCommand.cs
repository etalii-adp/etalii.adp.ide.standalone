using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.GartnerHypeCycleGraph;

/// <summary>Adds a trend where the toolbox's Trend was dropped (Requirement 11.1).</summary>
/// <param name="BodyPath">The document.</param>
/// <param name="X">The drop's x on the canvas. The trend starts at the start of the month it falls in.</param>
/// <param name="Y">The drop's y. The trend's middle lands on the nearest row.</param>
/// <param name="TrendId">
/// Empty to have one minted. The handler mints it ONCE and returns the minted command as the redo, so
/// a redone trend keeps the id later commands refer to.
/// </param>
public sealed record AddGhgTrendCommand(string BodyPath, double X, double Y, string TrendId = "") : ICommand;
