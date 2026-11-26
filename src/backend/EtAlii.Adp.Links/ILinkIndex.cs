using EtAlii.Adp.Specification.Fbl.Links;

namespace EtAlii.Adp.Links;

/// <summary>
/// The index of every workspace root a connection has open (FBL 17.3): for each, the documents of every tool type that keeps a
/// document id, read when the root is first tracked, read again on its file events, and taken out when they are deleted.
/// Links are resolved against the workspace of the root that holds the consuming document.
/// </summary>
public interface ILinkIndex
{
    /// <summary>
    /// Indexes every document under <paramref name="rootPath"/> and follows its changes until the returned handle is disposed;
    /// a root tracked twice is read once and followed until both are disposed.
    /// </summary>
    IDisposable Track(string rootPath);

    /// <summary>The workspace of the tracked root that holds <paramref name="path"/>, the innermost when roots nest; null when none does.</summary>
    LinkWorkspace? WorkspaceOf(string path);

    /// <summary>
    /// Gives each document at <paramref name="paths"/> a new document id and writes it, as the fix of <c>link.ambiguous-document</c>
    /// does to every candidate but the chosen one (FBL 17.6). A document that is open hears it as any change made outside it.
    /// Returns why one could not be given one, or null when every one was.
    /// </summary>
    Task<string?> RenewAsync(IReadOnlyList<string> paths, CancellationToken cancellationToken);

    /// <summary>
    /// Raised after a document was read again or taken out, outside any lock: what an open diagram follows so that its linked
    /// elements show their targets as they now are (DISL 4.12).
    /// </summary>
    event EventHandler<LinkIndexChangedEventArgs>? Changed;
}

/// <summary>The path of a document that changed in the index.</summary>
public sealed class LinkIndexChangedEventArgs(string path) : EventArgs
{
    /// <summary>The document's path, with forward slashes.</summary>
    public string Path { get; } = path;
}
