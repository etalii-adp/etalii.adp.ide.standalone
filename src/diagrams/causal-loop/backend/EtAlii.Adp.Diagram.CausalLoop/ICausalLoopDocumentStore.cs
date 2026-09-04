namespace EtAlii.Adp.Diagram.CausalLoop;

/// <summary>
/// The one owner of loaded <c>.cld</c> files, keyed by path, so two connections on one document
/// share its parse rather than each holding their own.
/// </summary>
public interface ICausalLoopDocumentStore
{
    /// <summary>The document at <paramref name="path"/>, loaded once and kept.</summary>
    CausalLoopDocumentEntry GetOrLoad(string path);

    /// <summary>Forgets a document, so the next open reads it afresh.</summary>
    void Forget(string path);

    /// <summary>Re-reads a document something outside changed, and tells the sessions on it.</summary>
    void Reload(string path);

    /// <summary>Raised after a change is picked up.</summary>
    event EventHandler<CausalLoopDocumentChangedEventArgs>? Changed;
}
