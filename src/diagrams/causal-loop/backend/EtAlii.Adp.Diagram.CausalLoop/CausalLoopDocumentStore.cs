using System.Collections.Concurrent;
using EtAlii.Adp.Backend.Hierarchy;

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
