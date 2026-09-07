using System.Collections.Concurrent;
using EtAlii.Adp.Documents;

namespace EtAlii.Adp.Diagram.CausalLoop;

/// <inheritdoc cref="ICausalLoopDocumentStore" />
public sealed class CausalLoopDocumentStore : ICausalLoopDocumentStore
{
    private readonly ConcurrentDictionary<string, CausalLoopDocumentEntry> _entries =
        new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public event EventHandler<CausalLoopDocumentChangedEventArgs>? Changed;

    /// <inheritdoc />
    public CausalLoopDocumentEntry GetOrLoad(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return _entries.GetOrAdd(path, Load);
    }

    /// <inheritdoc />
    public string Save(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (!_entries.TryGetValue(path, out var entry))
        {
            return "This causal loop diagram is not loaded, so there is nothing to save.";
        }

        try
        {
            // The central writer rather than a raw write: it publishes through a scratch file so
            // a reader never sees a half-written document, which a guard enforces tree-wide.
            AdpFileWriter.Save(path, entry.Document.Text);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return $"This causal loop diagram could not be saved: {exception.Message}";
        }

        // Re-parsed from the document just written, so the model and the bytes cannot disagree.
        var parsed = CausalLoopParser.Parse(entry.Document);
        _entries[path] = entry with { Model = parsed.Model, Problems = parsed.Problems };
        return "";
    }

    /// <inheritdoc />
    public void Forget(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _entries.TryRemove(path, out _);
    }

    /// <inheritdoc />
    public void Reload(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        _entries[path] = Load(path);
        Changed?.Invoke(this, new CausalLoopDocumentChangedEventArgs(path));
    }

    private static CausalLoopDocumentEntry Load(string path)
    {
        string text;
        try
        {
            // The central reader rather than File.ReadAllText: a raw read opens at
            // FileShare.Read and loses to a concurrent save, which a guard in the backend
            // suite enforces across every production file.
            text = SharedDocumentReader.ReadAllText(path);
        }
        catch (IOException exception)
        {
            return CausalLoopDocumentEntry.Unreadable($"This causal loop diagram could not be read: {exception.Message}");
        }
        catch (UnauthorizedAccessException exception)
        {
            return CausalLoopDocumentEntry.Unreadable($"This causal loop diagram could not be read: {exception.Message}");
        }

        var document = CausalLoopDocument.Parse(text);
        var parsed = CausalLoopParser.Parse(document);
        return new CausalLoopDocumentEntry(document, parsed.Model, parsed.Problems, "");
    }
}
