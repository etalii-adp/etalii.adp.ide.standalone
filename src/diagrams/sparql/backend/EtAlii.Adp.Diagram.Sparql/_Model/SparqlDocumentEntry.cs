namespace EtAlii.Adp.Diagram.Sparql;

/// <summary>
/// One loaded query file: its text exactly as read, the model it parsed to, and - where it did
/// not parse - why. There is no document type with edit operations here, deliberately: nothing
/// in this module writes a <c>.rq</c>, so plain text plus a model is the whole need.
/// </summary>
/// <param name="Text">The file's text, byte-for-byte as read - and byte-for-byte as it will stay.</param>
/// <param name="Model">What the query asks; <see cref="SparqlQueryModel.Empty"/> where it could not be parsed.</param>
/// <param name="Error">Why the file could not be parsed - naming SPARQL Update when that is what it is; empty when it parsed.</param>
/// <param name="ErrorLine">The line the parser stopped at, 1-based; 0 when there was no error.</param>
public sealed record SparqlDocumentEntry(
    string Text,
    SparqlQueryModel Model,
    string Error,
    int ErrorLine)
{
    /// <summary>Whether the file parsed, and so whether there is a query to draw.</summary>
    public bool IsUsable => Error.Length == 0;
}
