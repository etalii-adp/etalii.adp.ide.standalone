using System.Collections.Concurrent;
using Serilog;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.Rdf;

/// <inheritdoc cref="IRdfDocumentStore" />
public sealed class RdfDocumentStore : IRdfDocumentStore
{
    private static readonly ILogger _logger = Log.ForContext<RdfDocumentStore>();

    private readonly ConcurrentDictionary<string, RdfDocumentEntry> _entries = new(StringComparer.OrdinalIgnoreCase);

    // The paths this store is writing right now, so its own save does not bounce back through
    // Reload as an "external" change - the per-path saving guard every sibling store carries.
    private readonly ConcurrentDictionary<string, byte> _selfWrites = new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public event EventHandler<RdfDocumentChangedEventArgs>? Changed;

    /// <inheritdoc />
    public RdfDocumentEntry GetOrLoad(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return _entries.GetOrAdd(path, Load);
    }

    /// <inheritdoc />
    public string Save(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var entry = GetOrLoad(path);
        if (!entry.IsUsable)
        {
            // Writing a document whose model is empty because it never parsed would replace a
            // file somebody can still fix with one this module invented.
            _logger.Warning("Refusing to write {Path}: it does not parse ({Error})", path, entry.Error);
            return $"{IoPath.GetFileName(path)} does not parse, so it was not written. {entry.Error}";
        }

        _selfWrites[path] = 1;
        try
        {
            var directory = IoPath.GetDirectoryName(path);
            if (directory is { Length: > 0 } && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(path, entry.Document.Text);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The edit stays in memory: losing it because the disk refused would be worse than a
            // save the user can retry once the file is writable again.
            _logger.Warning(exception, "Could not write {Path}; the change is kept in memory", path);
            return $"{IoPath.GetFileName(path)} could not be written. The change is still here to try again.";
        }
        finally
        {
            _selfWrites.TryRemove(path, out _);
        }

        // The document's own lines are authoritative and unchanged by writing them out, but what
        // they mean has changed - so the model is rebuilt from the document rather than re-read.
        var reparsed = Parse(path, entry.Document);
        _entries[path] = reparsed;
        Changed?.Invoke(this, new RdfDocumentChangedEventArgs(path, reparsed));
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

        if (_selfWrites.ContainsKey(path))
        {
            // The change on disk is this store's own save, mid-write; Save reparses and tells
            // the sessions itself.
            return;
        }

        _entries.TryRemove(path, out _);
        var entry = GetOrLoad(path);
        Changed?.Invoke(this, new RdfDocumentChangedEventArgs(path, entry));
    }

    private RdfDocumentEntry Load(string path)
    {
        string text;
        try
        {
            // A file that does not exist yet is an empty document, not an error: it may have
            // been created a moment ago, and a diagram that cannot open at all is the worse
            // answer.
            text = File.Exists(path) ? File.ReadAllText(path) : "";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.Warning(exception, "Could not read {Path}; opening it as empty", path);
            text = "";
        }

        return Parse(path, RdfDocument.Parse(text));
    }

    /// <summary>
    /// What the document states, or the reason nothing could be read from it. A file that is not
    /// Turtle is an ordinary state for a file somebody is editing, so it is carried as an entry
    /// with an error rather than thrown out of the store (Requirement 1.5).
    /// </summary>
    private static RdfDocumentEntry Parse(string path, RdfDocument document)
    {
        try
        {
            return new RdfDocumentEntry(document, RdfParser.Parse(document), "", 0);
        }
        catch (RdfParseException exception)
        {
            _logger.Debug(exception, "{Path} does not parse at line {Line}", path, exception.Line);
            return new RdfDocumentEntry(document, RdfModel.Empty, exception.Message, exception.Line);
        }
    }
}
