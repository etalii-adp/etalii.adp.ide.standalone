using EtAlii.Adp.Backend;

namespace EtAlii.Adp.Diagram.Timeline;

/// <summary>
/// Connects one element to another. A second connection between the same pair is permitted -
/// the notation carries a label per connection, and two relationships between one pair are
/// meaningful (Requirement 8.8).
/// </summary>
/// <param name="BodyPath">The document.</param>
/// <param name="Id">The new connection's id, the caller's to supply so a redo reuses it.</param>
/// <param name="From">The source element's id.</param>
/// <param name="To">The target element's id.</param>
/// <param name="Label">What the author calls it, or empty.</param>
public sealed record ConnectTimelineElementsCommand(
    string BodyPath,
    string Id,
    string From,
    string To,
    string Label) : ICommand;
