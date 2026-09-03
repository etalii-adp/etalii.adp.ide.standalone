namespace EtAlii.Adp.Diagram.Rdf;

/// <summary>
/// Says why a document is not valid Turtle or N-Triples, and where. The store catches this and
/// carries it as the entry's error, so a broken file opens as unavailable rather than not at all.
/// </summary>
public sealed class RdfParseException(string message, int line) : Exception(message)
{
    /// <summary>The line the parser stopped at, 1-based, ready to show a person.</summary>
    public int Line { get; } = line;
}
