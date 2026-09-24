namespace EtAlii.Adp.Editor;

/// <summary>
/// One open text file, from an editor module's point of view: its current text, and word of
/// a change on disk. Deliberately smaller than a diagram session - a text file has no viewport
/// and no elements to move (modular-text-editors design, Components). Saving is not the
/// session's: every editor's save is the shared <c>SaveTextFileCommand</c>, whose handler writes
/// through its own <see cref="TextFileBuffer"/> - so the session hears its own save as a change,
/// which is how an open text view learns what was written.
/// </summary>
public interface IEditorSession : IAsyncDisposable
{
    /// <summary>The file's current text.</summary>
    string Content { get; }

    /// <summary>Raised when the file changed underneath the session - an external edit.</summary>
    event EventHandler<EditorContentChangedEventArgs>? Changed;
}
