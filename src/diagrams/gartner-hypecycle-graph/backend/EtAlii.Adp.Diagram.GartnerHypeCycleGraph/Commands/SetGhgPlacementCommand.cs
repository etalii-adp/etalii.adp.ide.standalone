using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.GartnerHypeCycleGraph;

/// <summary>
/// Moves a trend, a trigger or a note so its TOP-LEFT is (<paramref name="X"/>, <paramref name="Y"/>),
/// as the canvas's move sends it. A trend's left edge snaps to the nearest step and its top to the
/// nearest row; a trigger's CENTRE does, to a step line and a row's middle; a note's top-left does.
/// </summary>
/// <remarks>
/// A trend's move keeps the span, and shifts every stored boundary by exactly the months the trend
/// moved (Requirement 3.5).
/// </remarks>
public sealed record SetGhgPlacementCommand(string BodyPath, string ElementId, double X, double Y) : ICommand;
