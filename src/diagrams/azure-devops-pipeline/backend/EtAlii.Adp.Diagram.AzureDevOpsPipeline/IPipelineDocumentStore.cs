using EtAlii.Adp.Context;
using EtAlii.Adp.Documents;

namespace EtAlii.Adp.Diagram.AzureDevOpsPipeline;

/// <summary>
/// The one owner of pipeline documents on disk.
/// </summary>
/// <remarks>
/// <para>
/// One document per open pipeline, keyed by path, so two diagrams on the same file share an
/// instance of it and an edit made through one is visible in the other without a reload.
/// </para>
/// <para>
/// <b>Opening and saving carry the workspace root as well as the path</b>, which no other module's
/// store needs, and which is why this interface does not extend
/// <see cref="IDiagramDocumentStore{TBody, TModel}"/>: a pipeline is not necessarily one file, its
/// templates are resolved relative to the root and may only be read from inside it, so the root is
/// part of the question "what does this file say?" and not an ambient setting. A reload, a deletion
/// and a forget reach a document already opened, whose root the store recorded, so those are the
/// shared <see cref="IDiagramDocumentStore"/> calls and the shared reloader serves this module too.
/// </para>
/// </remarks>
public interface IPipelineDocumentStore : IDiagramDocumentStore
{
    /// <summary>
    /// The pipeline at <paramref name="path"/>, loaded once and kept. A file that is not there yet
    /// yields an empty document rather than throwing, so a diagram whose body has not been written
    /// still opens and the first save creates it.
    /// </summary>
    DocumentEntry<PipelineBody, PipelineModel> GetOrLoad(string rootPath, string path);

    /// <summary>
    /// This store as seen from <paramref name="rootPath"/>: the shared store interface, whose calls
    /// name a path alone, answered with that root. It is how a session opened in a workspace stands
    /// on the session base.
    /// </summary>
    IDiagramDocumentStore<PipelineBody, PipelineModel> In(string rootPath);

    /// <summary>
    /// <paramref name="bytes"/> read as the pipeline at <paramref name="path"/> in <paramref name="rootPath"/> is:
    /// its templates followed from that root. An edit opens the copy of a body it changes with this, so the copy
    /// reads as the store's own does. Never throws on content.
    /// </summary>
    PipelineBody Open(string rootPath, string path, byte[] bytes);

    /// <summary>Raised after a save, and after an external change is picked up.</summary>
    event EventHandler<DocumentChangedEventArgs>? Changed;

    /// <summary>How the pipeline a context target names is read from <paramref name="documents"/>: from the target's root as well as its path.</summary>
    internal static Func<ContextTarget, DocumentEntry<PipelineBody, PipelineModel>> LoaderFor(IPipelineDocumentStore documents)
    {
        ArgumentNullException.ThrowIfNull(documents);
        return target => documents.GetOrLoad(target.RootPath, target.ResolvedFullPath);
    }
}
