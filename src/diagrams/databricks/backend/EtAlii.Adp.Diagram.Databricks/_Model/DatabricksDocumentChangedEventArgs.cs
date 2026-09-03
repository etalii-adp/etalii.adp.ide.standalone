namespace EtAlii.Adp.Diagram.Databricks;

/// <summary>Says which document changed and what it now holds - raised after a save, and after an external change is picked up.</summary>
public sealed class DatabricksDocumentChangedEventArgs(string path, DatabricksDocumentEntry entry) : EventArgs
{
    /// <summary>The changed file's path, as the store keys it.</summary>
    public string Path { get; } = path;

    /// <summary>The document as it now stands.</summary>
    public DatabricksDocumentEntry Entry { get; } = entry;
}
