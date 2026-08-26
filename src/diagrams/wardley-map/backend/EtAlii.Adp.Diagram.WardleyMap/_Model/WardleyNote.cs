namespace EtAlii.Adp.Diagram.WardleyMap;

/// <summary>
/// A `note` - free text at a position on the map (Requirement 6.7).
/// </summary>
/// <param name="Text">What it says.</param>
/// <param name="Position">Where it sits, in the document's own axis order.</param>
/// <param name="Line">The 1-based line that declares it.</param>
/// <remarks>
/// A note's text is also its identity key in the sidecar, because it is the only handle the
/// document offers - a note has no name. That is the weakest key this module uses, and editing
/// a note's text outside ADP loses its identity; the alternative was writing an ADP id into
/// someone else's file, which Requirement 3.7 forbids.
/// </remarks>
public sealed record WardleyNote(string Text, WardleyCoordinate Position, uint Line);
