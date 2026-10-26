using EtAlii.Adp.Documents;

namespace EtAlii.Adp.Diagram.SupplyChain.Tests.Parity;

/// <summary>A store holding one text, which records what is saved to it.</summary>
internal sealed class SupplyChainParityStore(string text) : ISupplyChainDocumentStore
{
    /// <summary>The text of the last save, or null.</summary>
    public string? Saved { get; private set; }

    public event EventHandler<DocumentChangedEventArgs>? Changed
    {
        add { }
        remove { }
    }

    public ValueTask<DocumentEntry<LineDocument, SupplyChainModel>> GetOrLoadAsync(string path, CancellationToken cancellationToken) => ValueTask.FromResult(Get(path));

    public DocumentEntry<LineDocument, SupplyChainModel> Get(string path)
    {
        var body = LineDocument.Parse(text);
        return new DocumentEntry<LineDocument, SupplyChainModel>(body, SupplyChainParser.Outcome(body));
    }

    public Task<DocumentSaveResult> SaveAsync(string path, LineDocument document, CancellationToken cancellationToken)
    {
        Saved = document.Text;
        return Task.FromResult(DocumentSaveResult.Ok);
    }

    public void Forget(string path)
    {
    }

    public Task BodyDeletedAsync(string path, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task ReloadAsync(string path, CancellationToken cancellationToken) => Task.CompletedTask;
}
