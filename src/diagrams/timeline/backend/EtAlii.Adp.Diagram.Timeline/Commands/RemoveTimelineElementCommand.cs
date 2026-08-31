using EtAlii.Adp.Backend;

namespace EtAlii.Adp.Diagram.Timeline;

/// <summary>
/// Removes one element and every connection that references it, as one command with one inverse
/// (Requirement 2.5).
/// </summary>
/// <param name="BodyPath">The document to remove from.</param>
/// <param name="ElementId">Which element.</param>
public sealed record RemoveTimelineElementCommand(
    string BodyPath,
    string ElementId) : ICommand;
