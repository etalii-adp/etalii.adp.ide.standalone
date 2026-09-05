using System.Collections.Concurrent;
using EtAlii.Adp.Backend.Hierarchy;
using Serilog;

namespace EtAlii.Adp.Diagram.C4;

/// <inheritdoc cref="IC4DocumentStore" />
public sealed class C4DocumentStore : IC4DocumentStore
{
    private static readonly ILogger _logger = Log.ForContext<C4DocumentStore>();

    private readonly ConcurrentDictionary<string, C4DocumentEntry> _entries = new(StringComparer.OrdinalIgnoreCase);

    // The paths this store is writing right now, so its own save does not bounce back through
    // Reload as an "external" change - PlainEditorSession's saving guard, per path.
    private readonly ConcurrentDictionary<string, byte> _selfWrites = new(StringComparer.OrdinalIgnoreCase);

    public event EventHandler<C4DocumentChangedEventArgs>? Changed;

    public C4Document GetOrLoad(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Loaded(path).Document;
    }

    public C4Workspace WorkspaceOf(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Loaded(path).Workspace;
    }

    public void Save(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var entry = Loaded(path);
        var text = entry.Document.ToText();
        _selfWrites[path] = 1;
        try
        {
            var directory = System.IO.Path.GetDirectoryName(path);
            if (directory is { Length: > 0 } && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            AdpFileWriter.Save(path, text);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The edit stays in memory: losing it because the disk refused would be worse than
            // a save the user can retry once the file is writable again.
            _logger.Warning(exception, "Could not write {Path}; the change is kept in memory", path);
            return;
        }
        finally
        {
            _selfWrites.TryRemove(path, out _);
        }

        var workspace = C4Parser.Parse(entry.Document);
        _entries[path] = new C4DocumentEntry(entry.Document, workspace);
        Changed?.Invoke(this, new C4DocumentChangedEventArgs(path, workspace));
    }

    public void Touch(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        Changed?.Invoke(this, new C4DocumentChangedEventArgs(path, Loaded(path).Workspace));
    }

    public void Forget(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _entries.TryRemove(path, out _);
    }

    /// <summary>Re-reads a document an external tool changed, and tells the sessions on it.</summary>
    public void Reload(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (_selfWrites.ContainsKey(path))
        {
            // The change on disk is this store's own save, mid-write; Save reparses and tells
            // the sessions itself.
            return;
        }

        _entries.TryRemove(path, out _);
        var entry = Loaded(path);
        Changed?.Invoke(this, new C4DocumentChangedEventArgs(path, entry.Workspace));
    }

    private C4DocumentEntry Loaded(string path) => _entries.GetOrAdd(path, Load);

    private static C4DocumentEntry Load(string path)
    {
        string text;
        try
        {
            // A body that does not exist yet is an empty document, not an error: the .adp file
            // may have been created a moment ago, and a diagram that cannot open at all is a
            // worse answer than an empty one.
            text = File.Exists(path) ? SharedDocumentReader.ReadAllText(path) : "";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.Warning(exception, "Could not read {Path}; opening it as empty", path);
            text = "";
        }

        var document = C4Document.Parse(text);
        return new C4DocumentEntry(document, C4Parser.Parse(document));
    }

}
