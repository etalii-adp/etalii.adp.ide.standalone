using EtAlii.Adp.Documents;
namespace EtAlii.Adp.Diagram.Timeline;

/// <summary>
/// The empty document a new timeline starts as: the schema marker and an empty element list,
/// and nothing else (Requirement 1.3).
/// </summary>
/// <remarks>
/// An empty timeline is a valid timeline - it opens showing the ruler and an empty canvas,
/// which is exactly what an author starts from. No sample content: a placeholder element would
/// be the first thing every user deletes.
/// </remarks>
public sealed class TimelineDocumentFactory : IDiagramDocumentFactory
{
    /// <inheritdoc />
    public DiagramOrigin Origin => Diagram.Timeline.Origin;

    /// <inheritdoc />
    public string CreateEmptyDocument(string baseName)
    {
        ArgumentNullException.ThrowIfNull(baseName);

        // The base name is not written into the document: a timeline has no title line in its
        // schema, and inventing one would put a key in every new file that nothing reads.
        //
        // CRLF and a trailing newline: the writer preserves whatever a file already uses
        // (Requirement 2.1), but a file ADP creates has no existing style to preserve, and CRLF
        // is the repository's own house style (.gitattributes, src/.editorconfig).
        return "timeline: 1\r\nelements: []\r\n";
    }
}
