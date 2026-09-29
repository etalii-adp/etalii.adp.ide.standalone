using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.GartnerHypeCycleGraph;

/// <summary>
/// Moves one inner phase boundary - a chevron dragged on the canvas, or a date typed in the grid - and
/// stores it as dragged (Requirement 4.6).
/// </summary>
/// <param name="BodyPath">The document.</param>
/// <param name="TrendId">The trend.</param>
/// <param name="Index">Which boundary: 0 ends the Peak, 1 the Trough, 2 the Slope.</param>
/// <param name="Month">The boundary's new date as <c>YYYY-MM</c>.</param>
/// <remarks>Clamped, not refused, so no phase becomes shorter than a month, as the canvas clamps it.</remarks>
public sealed record SetGhgBoundaryCommand(string BodyPath, string TrendId, int Index, string Month) : ICommand;
