namespace EtAlii.Adp.Diagram.Sparql;

/// <summary>Says which query file changed on disk and what it now holds.</summary>
public sealed class SparqlDocumentChangedEventArgs(string path, SparqlDocumentEntry entry) : EventArgs
{
    /// <summary>The changed file's path, as the store keys it.</summary>
    public string Path { get; } = path;

    /// <summary>The document as it now stands.</summary>
    public SparqlDocumentEntry Entry { get; } = entry;
}
