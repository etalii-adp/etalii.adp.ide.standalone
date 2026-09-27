using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.GartnerHypeCycleGraph;

/// <summary>
/// Moves a trend so its TOP-LEFT is (<paramref name="X"/>, <paramref name="Y"/>), as the canvas's move
/// sends it: the left edge snaps to the nearest month start and the top to the nearest row.
/// </summary>
/// <remarks>
/// A move keeps the span, and shifts every stored boundary by exactly the months the trend moved
/// (Requirement 3.5).
/// </remarks>
public sealed record SetGhgPlacementCommand(string BodyPath, string TrendId, double X, double Y) : ICommand;
