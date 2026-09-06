using System.Collections.Concurrent;
using EtAlii.Adp.Backend.Hierarchy;
using EtAlii.Adp.Common;
using Serilog;
using YamlDotNet.Core;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.DependencyGraph;

/// <inheritdoc cref="IDependencyGraphDocumentStore" />
public sealed class DependencyGraphDocumentStore : IDependencyGraphDocumentStore
{
    private static readonly ILogger _logger = Log.ForContext<DependencyGraphDocumentStore>();

    private readonly ConcurrentDictionary<string, DependencyGraphDocumentEntry> _entries =
        new(StringComparer.OrdinalIgnoreCase);

    // The paths this store is writing right now, so its own save does not bounce back through
    // Reload as an "external" change - PlainEditorSession's saving guard, per path.
    private readonly ConcurrentDictionary<string, byte> _selfWrites = new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public event EventHandler<DependencyGraphDocumentChangedEventArgs>? Changed;

    /// <inheritdoc />
    public DependencyGraphDocumentEntry GetOrLoad(string path)
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

            AdpFileWriter.Save(path, entry.Document.Text);
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
        Changed?.Invoke(this, new DependencyGraphDocumentChangedEventArgs(path, reparsed.Model));
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
        Changed?.Invoke(this, new DependencyGraphDocumentChangedEventArgs(path, entry.Model));
    }

    private DependencyGraphDocumentEntry Load(string path)
    {
        string text;
        try
        {
            // A file that does not exist yet is an empty document, not an error: it may have
            // been created a moment ago, and a diagram that cannot open at all is the worse
            // answer.
            text = File.Exists(path) ? SharedDocumentReader.ReadAllText(path) : "";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.Warning(exception, "Could not read {Path}; opening it as empty", path);
            text = "";
        }

        return Parse(path, LineDocument.Parse(text));
    }

    /// <summary>
    /// The model for a document, or the reason there is none. A file that is not YAML is an
    /// ordinary state for a file somebody is editing, so it is carried as an entry with an error
    /// rather than thrown out of the store.
    /// </summary>
    private static DependencyGraphDocumentEntry Parse(string path, LineDocument document)
    {
        try
        {
            return new DependencyGraphDocumentEntry(document, DependencyGraphParser.Parse(document), "", 0);
        }
        catch (YamlException exception)
        {
            var line = (int)exception.Start.Line;
            _logger.Debug(exception, "{Path} does not parse at line {Line}", path, line);
            return new DependencyGraphDocumentEntry(
                document,
                DependencyGraphModel.Empty,
                exception.Message,
                Math.Max(line, 1));
        }
    }
}
