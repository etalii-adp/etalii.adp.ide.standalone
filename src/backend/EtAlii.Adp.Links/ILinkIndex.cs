using EtAlii.Adp.Specification.Fbl.Links;

namespace EtAlii.Adp.Links;

/// <summary>
/// The index of every workspace root a connection has open (FBL 17.3): for each, the documents of every tool type that keeps a
/// document id, read when the root is first tracked, read again on its file events and as an open document is edited, and
/// taken out when they are deleted. Links are resolved against the workspace of the root that holds the consuming document.
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

    /// <summary>What an open document at <paramref name="path"/> holds now, saved or not: the index holds that in place of the file.</summary>
    void Edited(string path, byte[] bytes);

    /// <summary>The open document at <paramref name="path"/> was closed without being saved: the index holds the file again.</summary>
    void Reverted(string path);

    /// <summary>A document of a tracked root changed in the index, by its path: added, read again or taken out.</summary>
    event EventHandler<LinkIndexChangedEventArgs>? Changed;
}

/// <summary>The path of a document that changed in the index.</summary>
public sealed class LinkIndexChangedEventArgs(string path) : EventArgs
{
    /// <summary>The document's path, with forward slashes.</summary>
    public string Path { get; } = path;
}
