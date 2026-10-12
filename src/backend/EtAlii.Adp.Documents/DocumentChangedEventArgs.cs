namespace EtAlii.Adp.Documents;

/// <summary>The document at <paramref name="path"/> was replaced, for the sessions on it.</summary>
/// <remarks>One type for every store, in place of one per module (code-structure FR-029).</remarks>
public sealed class DocumentChangedEventArgs(string path) : EventArgs
{
    /// <summary>The body path whose document changed.</summary>
    public string Path { get; } = path;
}
