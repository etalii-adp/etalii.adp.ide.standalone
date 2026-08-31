using EtAlii.Adp.Backend;

namespace EtAlii.Adp.Diagram.Timeline;

/// <summary>
/// Gives an element an end, changes the one it has, or removes it - which is how a moment
/// becomes a period and back (Requirements 7.5, 11.3).
/// </summary>
/// <remarks>
/// Separate from <see cref="SetTimelinePlacementCommand"/> deliberately: a placement never
/// changes what an element <em>is</em>, so a drag cannot turn a moment into a period by
/// accident. Changing kind is its own intent, reached only through the context menu.
/// </remarks>
/// <param name="BodyPath">The document.</param>
/// <param name="ElementId">Which element.</param>
/// <param name="End">The new end as document text, or null to remove it.</param>
public sealed record SetTimelineEndCommand(
    string BodyPath,
    string ElementId,
    string? End) : ICommand;
