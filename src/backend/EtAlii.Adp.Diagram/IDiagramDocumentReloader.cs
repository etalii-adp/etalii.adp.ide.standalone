namespace EtAlii.Adp.Diagram;

/// <summary>
/// Re-reads one diagram document after something outside its own store changed it on disk -
/// a text-editor save through "Open as text", a git checkout, any external tool. A module
/// registers one per origin it serves, wrapping its document store's <c>ReloadAsync</c>; the
/// <see cref="DiagramDocumentReloadBridge"/> resolves it by origin and never learns what a
/// document of that type is (modular-text-editors Requirements 5.3, 5.5: an external write
/// reaches every open diagram as ordinary pushed deltas, because the file on disk is the
/// tie-breaker, always).
/// </summary>
public interface IDiagramDocumentReloader
{
    /// <summary>The diagram type whose documents this reloads.</summary>
    ToolOrigin Origin { get; }

    /// <summary>
    /// Re-reads the document whose body is at <paramref name="bodyPath"/>, within the project
    /// rooted at <paramref name="rootPath"/>, and tells the sessions on it. The store's own
    /// in-flight save is not an external change and must not bounce back through here - each
    /// store suppresses that itself, the way <c>TextEditorSession</c>'s saving guard does.
    /// </summary>
    /// <remarks>Read without holding a thread (code-structure FR-050): a body being written is retried on the clock, not slept on.</remarks>
    Task ReloadAsync(string rootPath, string bodyPath, CancellationToken cancellationToken);

    /// <summary>
    /// The body at <paramref name="bodyPath"/> was deleted, or moved to a name that is not a
    /// save's own backup - the watcher's evidence that it is gone, not a read that failed. A store
    /// that keeps its last good document through a failed reload needs this to tell the two apart:
    /// a publish in flight leaves the body missing for an instant too, but raises no delete of it.
    /// A store with no such rule gets a reload, which is what a delete always was.
    /// </summary>
    Task BodyDeletedAsync(string rootPath, string bodyPath, CancellationToken cancellationToken) => ReloadAsync(rootPath, bodyPath, cancellationToken);
}
