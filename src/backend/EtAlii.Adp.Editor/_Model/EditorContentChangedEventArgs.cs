namespace EtAlii.Adp.Editor;

/// <summary>An external edit's payload: the file's new text, already re-read by the session.</summary>
public sealed class EditorContentChangedEventArgs(string content) : EventArgs
{
    public string Content { get; } = content;
}
