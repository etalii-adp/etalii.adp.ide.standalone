namespace EtAlii.Adp.Diagram.Sparql;

/// <summary>
/// Says why a document is not a SPARQL 1.1 query, and where. The store catches this and carries
/// it as the entry's error, so a broken file opens as unavailable rather than not at all - and a
/// SPARQL Update document is refused through this same door, by name.
/// </summary>
public sealed class SparqlParseException(string message, int line) : Exception(message)
{
    /// <summary>The line the parser stopped at, 1-based, ready to show a person.</summary>
    public int Line { get; } = line;
}
