namespace EtAlii.Adp.Editor;

/// <summary>
/// One open text file, from an editor module's point of view: its current text, a save, and
/// word of an external change. Deliberately smaller than a diagram session - a text file has
/// no viewport and no elements to move (modular-text-editors design, Components).
/// </summary>
public interface IEditorSession : IAsyncDisposable
{
    /// <summary>The file's current text.</summary>
    string Content { get; }

    /// <summary>Writes <paramref name="newContent"/>; returns an empty string, or the reason the save failed.</summary>
    Task<string> SaveAsync(string newContent, CancellationToken cancellationToken = default);

    /// <summary>Raised when the file changed underneath the session - an external edit.</summary>
    event EventHandler<EditorContentChangedEventArgs>? Changed;
}
