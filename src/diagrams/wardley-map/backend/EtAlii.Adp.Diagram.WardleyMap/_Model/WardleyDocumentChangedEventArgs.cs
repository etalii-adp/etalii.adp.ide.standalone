namespace EtAlii.Adp.Diagram.WardleyMap;

/// <summary>What changed about a Wardley map document, for the sessions that show it.</summary>
/// <param name="Path">The `.owm` file that changed.</param>
/// <remarks>
/// Carries the path alone for now. Once the parser exists this gains the re-derived map, so a
/// session can diff against what it last delivered without re-reading the file itself - the
/// shape <c>C4DocumentChangedEventArgs</c> already has.
/// </remarks>
public sealed record WardleyDocumentChangedEventArgs(string Path);
