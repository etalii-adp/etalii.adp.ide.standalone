using EtAlii.Adp.Backend;


using EtAlii.Adp.Common;
namespace EtAlii.Adp.Diagram.Timeline;

/// <summary>
/// Removes one element and every connection that references it, as one command with one inverse
/// (Requirement 2.5).
/// </summary>
/// <param name="BodyPath">The document to remove from.</param>
/// <param name="ElementId">Which element.</param>
/// <param name="RemoveEmptiedConnectionsSection">Whether an emptied <c>connections:</c> key goes too - set by an inverse undoing the insert that created it.</param>
public sealed record RemoveTimelineElementCommand(
    string BodyPath,
    string ElementId,
    bool RemoveEmptiedConnectionsSection = false) : ICommand;
