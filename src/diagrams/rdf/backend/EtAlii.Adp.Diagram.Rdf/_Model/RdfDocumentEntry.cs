using EtAlii.Adp.Documents;

namespace EtAlii.Adp.Diagram.Rdf;

/// <summary>
/// One loaded RDF file: the document, the model it parsed to, and - where it did not parse - why.
/// </summary>
/// <remarks>
/// The failure is carried rather than thrown because a file that does not parse still has to
/// open: the diagram shows as unavailable naming the file and line, and every edit is withheld
/// so a broken file is never made worse (Requirement 1.5).
/// </remarks>
/// <param name="Document">The lines, exactly as read.</param>
/// <param name="Model">What the file states; <see cref="RdfModel.Empty"/> where it could not be parsed.</param>
/// <param name="Error">Why the file could not be parsed; empty when it could.</param>
/// <param name="ErrorLine">The line the parser stopped at, 1-based; 0 when there was no error.</param>
public sealed record RdfDocumentEntry(
    LineDocument Document,
    RdfModel Model,
    string Error,
    int ErrorLine)
{
    /// <summary>Whether the file parsed, and so whether it may be edited.</summary>
    public bool IsUsable => Error.Length == 0;
}
