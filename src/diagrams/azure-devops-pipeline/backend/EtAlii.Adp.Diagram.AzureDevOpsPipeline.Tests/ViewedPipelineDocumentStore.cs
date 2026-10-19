using EtAlii.Adp.Documents;
using EtAlii.Adp.Files;

namespace EtAlii.Adp.Diagram.AzureDevOpsPipeline.Tests;

/// <summary>
/// A pipeline store answered by the shared store interface of one root, for the handler contract,
/// which hands over a store of that shape and wraps it to add a warning to every save.
/// </summary>
/// <remarks>
/// Every call goes to <paramref name="view"/> whatever root it names, which is right for a test whose
/// commands all name the one root the view was made for. A copy of a body is opened by a store of its
/// own, which opens it as the view's store would.
/// </remarks>
internal sealed class ViewedPipelineDocumentStore(IDiagramDocumentStore<PipelineBody, PipelineModel> view) : IPipelineDocumentStore
{
    private readonly PipelineDocumentStore _reader = new(new FileWriter());

    public event EventHandler<DocumentChangedEventArgs>? Changed
    {
        add => view.Changed += value;
        remove => view.Changed -= value;
    }

    public ValueTask<DocumentEntry<PipelineBody, PipelineModel>> GetOrLoadAsync(string rootPath, string path, CancellationToken cancellationToken) =>
        view.GetOrLoadAsync(path, cancellationToken);

    public IDiagramDocumentStore<PipelineBody, PipelineModel> In(string rootPath) => view;

    public PipelineBody Open(string rootPath, string path, byte[] bytes) => _reader.Open(rootPath, path, bytes);

    public Task ReloadAsync(string path, CancellationToken cancellationToken) => view.ReloadAsync(path, cancellationToken);

    public Task BodyDeletedAsync(string path, CancellationToken cancellationToken) => view.BodyDeletedAsync(path, cancellationToken);

    public void Forget(string path) => view.Forget(path);
}
