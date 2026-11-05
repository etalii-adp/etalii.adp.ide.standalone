using EtAlii.Adp.Documents;

namespace EtAlii.Adp.Designer.HierarchicalRequirement;

/// <summary>
/// Where the module's commands tell the open pages that a file was written or restored, so they read it again at once,
/// as an edit of their own, rather than when the watcher gets round to it and takes it for a change from outside.
/// </summary>
internal interface IHierarchicalRequirementDocumentStore : IReloadableDocumentStore
{
    /// <summary>Raised with a file's path and the bytes a command is about to write there, before it writes them.</summary>
    event Action<string, byte[]>? Writing;

    /// <summary>Says that a command is about to write <paramref name="bytes"/> to <paramref name="path"/>.</summary>
    void WillWrite(string path, byte[] bytes);

    /// <summary>Raised with the path of a file that was just written through ADP.</summary>
    event Action<string>? Reloaded;
}
