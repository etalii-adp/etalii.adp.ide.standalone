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
    DocumentEntry<LineDocument, PipelineModel> GetOrLoad(string rootPath, string path);

    /// <summary>
    /// Writes the document back and tells every session on it. Refuses while the document does not
    /// parse, so a file that is already broken is never made worse.
    /// </summary>
    /// <returns><see cref="DocumentSaveResult.Ok"/> when it was written, or a failure whose <see cref="DocumentSaveResult.Error"/> says why it was not (backend-centralization R3.1).</returns>
    /// <remarks>
    /// <b>The entry is a parameter rather than something this looked up, and that is the fix for a
    /// data-loss defect.</b> It used to call <see cref="GetOrLoad"/> itself, so it wrote whatever was
    /// in the cache at save time - and a <see cref="IReloadableDocumentStore.Reload"/> replaced that entry. A reload landing between a
    /// command's edit and its save therefore discarded the edit and REPORTED SUCCESS, which put the
    /// command's inverse on the undo stack for a change that never happened. Passing the entry makes
    /// that ordering unrepresentable: what the caller edited is what gets written.
    /// </remarks>
    DocumentSaveResult Save(string rootPath, string path, DocumentEntry<LineDocument, PipelineModel> entry);

    /// <summary>Raised after a save, and after an external change is picked up.</summary>
    event EventHandler<DocumentChangedEventArgs>? Changed;
}
