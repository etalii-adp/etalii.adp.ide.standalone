namespace EtAlii.Adp.Diagram.WardleyMap;

/// <summary>
/// The empty map a new diagram starts as: a `title` line carrying the file's base name, and
/// nothing else (Requirement 1.4). A map with no components is a valid map - it opens showing
/// its two axes and the four evolution bands, which is exactly what an author starts from.
/// </summary>
/// <remarks>
/// What this emits parses cleanly through the real OnlineWardleyMaps parser, which is the bar
/// the task 2 corpus set for every `.owm` this module writes.
/// </remarks>
public sealed class WardleyDocumentFactory : IDiagramDocumentFactory
{
    public DiagramOrigin Origin => Diagram.WardleyMap.Origin;

    public string CreateEmptyDocument(string baseName)
    {
        ArgumentNullException.ThrowIfNull(baseName);

        // LF and a trailing newline: the writer preserves whatever a file already uses
        // (Requirement 3.1), but a file ADP creates has no existing style to preserve, and LF
        // is what every other tool in this ecosystem writes.
        return $"title {baseName}\n";
    }
}
