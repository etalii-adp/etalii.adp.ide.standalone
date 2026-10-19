using EtAlii.Adp.Documents;

namespace EtAlii.Adp.Diagram.CausalLoopDiagram.Tests;

/// <summary>
/// A store over texts held in memory, for a test that runs a command and reads what it wrote without a
/// file: every read opens the text as the real store does, and a save keeps the body's text.
/// </summary>
internal sealed class CausalLoopMemoryStore : ICausalLoopDocumentStore
{
    private readonly Dictionary<string, string> _texts = new(StringComparer.Ordinal);

    public event EventHandler<DocumentChangedEventArgs>? Changed
    {
        add { }
        remove { }
    }

    /// <summary>The text of <paramref name="path"/>: as it was put, or as the last save left it.</summary>
    public string this[string path]
    {
        get => _texts[path];
        init => _texts[path] = value;
    }

    public ValueTask<DocumentEntry<CausalLoopBody, CausalLoopModel>> GetOrLoadAsync(string path, CancellationToken cancellationToken) => ValueTask.FromResult(Open(path));

    public DocumentEntry<CausalLoopBody, CausalLoopModel>? Get(string path) => _texts.ContainsKey(path) ? Open(path) : null;

    public Task<DocumentSaveResult> SaveAsync(string path, CausalLoopBody document, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        _texts[path] = document.Text;
        return Task.FromResult(DocumentSaveResult.Ok);
    }

    public Task ReloadAsync(string path, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task BodyDeletedAsync(string path, CancellationToken cancellationToken)
    {
        _texts.Remove(path);
        return Task.CompletedTask;
    }

    public void Forget(string path)
    {
    }

    private DocumentEntry<CausalLoopBody, CausalLoopModel> Open(string path)
    {
        var body = CausalLoopBody.Parse(_texts[path]);
        return new DocumentEntry<CausalLoopBody, CausalLoopModel>(body, body.Read());
    }
}
