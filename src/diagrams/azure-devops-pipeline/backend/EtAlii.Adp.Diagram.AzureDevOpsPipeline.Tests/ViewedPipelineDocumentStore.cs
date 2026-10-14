using EtAlii.Adp.Documents;

namespace EtAlii.Adp.Diagram.AzureDevOpsPipeline.Tests;

/// <summary>
/// A pipeline store answered by the shared store interface of one root, for the handler contract,
/// which hands over a store of that shape and wraps it to add a warning to every save.
/// </summary>
/// <remarks>
/// Every call goes to <paramref name="view"/> whatever root it names, which is right for a test whose
/// commands all name the one root the view was made for. A model is read by a store of its own,
/// which reads it as the view's store would.
/// </remarks>
internal sealed class ViewedPipelineDocumentStore(IDiagramDocumentStore<LineDocument, PipelineModel> view) : IPipelineDocumentStore
{
    private readonly PipelineDocumentStore _reader = new();

    public event EventHandler<DocumentChangedEventArgs>? Changed
    {
        add => view.Changed += value;
        remove => view.Changed -= value;
    }

    public DocumentEntry<LineDocument, PipelineModel> GetOrLoad(string rootPath, string path) => view.GetOrLoad(path);

    public IDiagramDocumentStore<LineDocument, PipelineModel> In(string rootPath) => view;

    public PipelineModel ModelOf(string rootPath, string path, LineDocument document) => _reader.ModelOf(rootPath, path, document);

    public void Reload(string path) => view.Reload(path);

    public void BodyDeleted(string path) => view.BodyDeleted(path);

    public void Forget(string path) => view.Forget(path);
}
