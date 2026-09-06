using EtAlii.Adp.Backend;
using EtAlii.Adp.Common;
using EtAlii.Adp.Hierarchy;
namespace EtAlii.Adp.Diagram.Timeline;

/// <summary>
/// Puts removed lines back exactly where they were - the inverse a removal reports, so undoing
/// one restores the file byte for byte, comments and formatting included.
/// </summary>
/// <param name="BodyPath">The document to restore into.</param>
/// <param name="ElementId">The element the lines declared, so redo can remove it again by id.</param>
/// <param name="Segments">The captured runs, each with the index it sat at, ascending.</param>
public sealed record RestoreTimelineLinesCommand(
    string BodyPath,
    string ElementId,
    IReadOnlyList<LineSegment> Segments) : ICommand;
