using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.GartnerHypeCycleGraph;

/// <summary>
/// Changes a trend's start, stop or both: a resize from the canvas, or a date typed in the grid.
/// </summary>
/// <param name="BodyPath">The document.</param>
/// <param name="TrendId">The trend.</param>
/// <param name="Start">The new start as <c>YYYY-MM</c>, or null to keep it.</param>
/// <param name="Stop">The new stop as <c>YYYY-MM</c>, or null to keep it.</param>
/// <remarks>
/// Stored boundaries keep their proportion of the span, snapped to a month, and every phase stays at
/// least a month long (Requirement 3.5).
/// </remarks>
public sealed record SetGhgSpanCommand(string BodyPath, string TrendId, string? Start, string? Stop) : ICommand;
