using EtAlii.Adp.Common;
namespace EtAlii.Adp.Diagram.DependencyGraph;

/// <summary>
/// The empty document a new dependency graph starts as: the schema marker and an empty element
/// list, and nothing else.
/// </summary>
/// <remarks>
/// An empty graph is a valid graph - it opens on an empty canvas, which is exactly what an author
/// starts from. No sample content: a placeholder node would be the first thing every user
/// deletes.
/// </remarks>
public sealed class DependencyGraphDocumentFactory : IDiagramDocumentFactory
{
    /// <inheritdoc />
    public DiagramOrigin Origin => Diagram.DependencyGraph.Origin;

    /// <inheritdoc />
    public string CreateEmptyDocument(string baseName)
    {
        ArgumentNullException.ThrowIfNull(baseName);

        // The base name is not written into the document: a graph has no title line in its
        // schema, and inventing one would put a key in every new file that nothing reads.
        //
        // CRLF and a trailing newline: the writer preserves whatever a file already uses, but a
        // file ADP creates has no existing style to preserve, and CRLF is the repository's own
        // house style (.gitattributes, src/.editorconfig).
        return "dependencies: 1\r\nelements: []\r\n";
    }
}
