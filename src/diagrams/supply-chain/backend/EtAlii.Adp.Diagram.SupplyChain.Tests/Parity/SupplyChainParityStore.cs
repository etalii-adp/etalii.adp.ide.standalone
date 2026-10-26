using EtAlii.Adp.Documents;

namespace EtAlii.Adp.Diagram.SupplyChain.Tests.Parity;

/// <summary>A store holding one text, which records what is saved to it: the module's, read as the module reads it.</summary>
internal sealed class SupplyChainParityStore(string text) : ParityStore<SupplyChainBody>(text), ISupplyChainDocumentStore;

/// <summary>A store holding one text as the lines the hand-written oracle (<see cref="HandWrittenSupplyChainEdits"/>) edits.</summary>
internal sealed class HandWrittenParityStore(string text) : ParityStore<LineDocument>(text);

/// <summary>A store holding one text, which records what is saved to it.</summary>
internal abstract class ParityStore<TBody>(string text) : IDiagramDocumentStore<TBody, SupplyChainModel>
    where TBody : ITextBody<TBody>
{
    /// <summary>The text of the last save, or null.</summary>
    public string? Saved { get; private set; }

    public event EventHandler<DocumentChangedEventArgs>? Changed
    {
        add { }
        remove { }
    }

    public ValueTask<DocumentEntry<TBody, SupplyChainModel>> GetOrLoadAsync(string path, CancellationToken cancellationToken) => ValueTask.FromResult(Get(path));

    public DocumentEntry<TBody, SupplyChainModel> Get(string path)
    {
        var body = TBody.Parse(text);
        return new DocumentEntry<TBody, SupplyChainModel>(body, SupplyChainReading.Outcome(body.Text));
    }

    public Task<DocumentSaveResult> SaveAsync(string path, TBody document, CancellationToken cancellationToken)
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
