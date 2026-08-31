namespace EtAlii.Adp.Diagram.Timeline;

/// <summary>Raised by the store when a document's content has changed, for the sessions on it.</summary>
/// <param name="path">The document that changed.</param>
/// <param name="model">What it now says.</param>
public sealed class TimelineDocumentChangedEventArgs(string path, TimelineModel model) : EventArgs
{
    /// <summary>The document that changed.</summary>
    public string Path { get; } = path;

    /// <summary>What it now says.</summary>
    public TimelineModel Model { get; } = model;
}
