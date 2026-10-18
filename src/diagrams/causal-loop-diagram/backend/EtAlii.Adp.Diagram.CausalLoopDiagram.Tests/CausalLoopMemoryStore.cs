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

    public DocumentEntry<CausalLoopBody, CausalLoopModel> GetOrLoad(string path)
    {
        var body = CausalLoopBody.Parse(_texts[path]);
        return new DocumentEntry<CausalLoopBody, CausalLoopModel>(body, body.Read());
    }

    public Task<DocumentSaveResult> SaveAsync(string path, CausalLoopBody document, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        _texts[path] = document.Text;
        return Task.FromResult(DocumentSaveResult.Ok);
    }

    public void Reload(string path)
    {
    }

    public void BodyDeleted(string path) => _texts.Remove(path);

    public void Forget(string path)
    {
    }
}
