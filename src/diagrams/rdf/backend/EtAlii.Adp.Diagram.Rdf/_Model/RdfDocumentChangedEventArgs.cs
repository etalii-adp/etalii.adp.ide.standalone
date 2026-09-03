namespace EtAlii.Adp.Diagram.Rdf;

/// <summary>Says which document changed and what it now holds - raised after a save, and after an external change is picked up.</summary>
public sealed class RdfDocumentChangedEventArgs(string path, RdfDocumentEntry entry) : EventArgs
{
    /// <summary>The changed file's path, as the store keys it.</summary>
    public string Path { get; } = path;

    /// <summary>The document as it now stands.</summary>
    public RdfDocumentEntry Entry { get; } = entry;
}
