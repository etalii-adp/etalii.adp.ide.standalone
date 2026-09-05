namespace EtAlii.Adp.Diagram.CausalLoop;

/// <summary>Raised after a document is re-read, so the sessions open on it can catch up.</summary>
/// <param name="path">The body file that changed.</param>
public sealed class CausalLoopDocumentChangedEventArgs(string path) : EventArgs
{
    /// <summary>The body file that changed.</summary>
    public string Path { get; } = path;
}
