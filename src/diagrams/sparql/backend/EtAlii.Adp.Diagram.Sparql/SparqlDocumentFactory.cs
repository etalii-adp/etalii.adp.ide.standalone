namespace EtAlii.Adp.Diagram.Sparql;

/// <summary>
/// The minimal query a new SPARQL diagram starts as: a starter <c>SELECT</c> that parses and
/// validates clean, so a file ADP creates opens as a diagram rather than as a finding.
/// </summary>
/// <remarks>
/// <para>
/// <b>This exists against the design's stated intent, and the reason is worth recording.</b>
/// The design said this module would register no document factory, because queries are authored
/// in text editors. Core disagrees at a level a module cannot argue with: a definition that
/// declares a document extension and registers no factory is a startup error naming the type
/// (<c>DiagramDocumentFactories.Verify</c>), so with no factory the host does not boot at all.
/// </para>
/// <para>
/// The read-only position is untouched by this. A factory supplies the initial text of a file
/// being created; it never rewrites one that exists, and this module still has no writer, no
/// command and no editing gesture. What a user gets from Add is a starter query to open in their
/// editor - which is where the design says queries are written, and now says it with a file in
/// hand rather than with a refusal.
/// </para>
/// <para>
/// CRLF and a trailing newline: a file ADP creates has no existing style to preserve, and CRLF
/// is the repository's house style.
/// </para>
/// </remarks>
public sealed class SparqlDocumentFactory(DiagramOrigin origin) : IDiagramDocumentFactory
{
    /// <inheritdoc />
    public DiagramOrigin Origin { get; } = origin;

    /// <inheritdoc />
    public string CreateEmptyDocument(string baseName)
    {
        ArgumentNullException.ThrowIfNull(baseName);

        // Every prefix declared here is used below, so the new file reports nothing at all -
        // not even the unused-prefix info the validator would otherwise be right to raise.
        return "PREFIX rdfs: <http://www.w3.org/2000/01/rdf-schema#>\r\n"
            + "\r\n"
            + "SELECT ?subject ?label\r\n"
            + "WHERE {\r\n"
            + "  ?subject rdfs:label ?label .\r\n"
            + "}\r\n"
            + "LIMIT 100\r\n";
    }
}
