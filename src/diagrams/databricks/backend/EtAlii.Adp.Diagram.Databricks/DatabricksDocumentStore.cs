using System.Collections.Concurrent;
using EtAlii.Adp.Common;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Hierarchy;
using Serilog;
using YamlDotNet.Core;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.Databricks;

/// <inheritdoc cref="IDatabricksDocumentStore" />
public sealed class DatabricksDocumentStore : IDatabricksDocumentStore
{
    private static readonly ILogger _logger = Log.ForContext<DatabricksDocumentStore>();

    private readonly ConcurrentDictionary<string, DatabricksDocumentEntry> _entries = new(StringComparer.OrdinalIgnoreCase);

    // The paths this store is writing right now, so its own save does not bounce back through
    // Reload as an "external" change - the per-path saving guard every sibling store carries.
    private readonly ConcurrentDictionary<string, byte> _selfWrites = new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public event EventHandler<DatabricksDocumentChangedEventArgs>? Changed;

    /// <inheritdoc />
    public DatabricksDocumentEntry GetOrLoad(string path)
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
            // Writing a document whose models are empty because it never parsed would replace a
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
        // they mean has changed - so the models are rebuilt from the document rather than re-read.
        var reparsed = Parse(path, entry.Document);
        _entries[path] = reparsed;
        Changed?.Invoke(this, new DatabricksDocumentChangedEventArgs(path, reparsed));
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
        Changed?.Invoke(this, new DatabricksDocumentChangedEventArgs(path, entry));
    }

    private DatabricksDocumentEntry Load(string path)
    {
        string text;
        try
        {
            // A file that does not exist yet is an empty document, not an error: it may have
            // been created a moment ago, and a diagram that cannot open at all is the worse
            // answer. The File.Exists check is what produces that, and it is load-bearing:
            // SharedDocumentReader opens with FileMode.Open and throws on a missing file, so
            // deleting the check would turn every open of a not-yet-created body into an
            // exception - caught below, and so still empty, but logged as a failure to read
            // what is an ordinary state.
            text = File.Exists(path) ? SharedDocumentReader.ReadAllText(path) : "";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.Warning(exception, "Could not read {Path}; opening it as empty", path);
            text = "";
        }

        return Parse(path, DatabricksDocument.Parse(text));
    }

    /// <summary>
    /// The family's three readings of a document, or the reason there are none. A file that is
    /// not YAML (or JSON - the same door) is an ordinary state for a file somebody is editing,
    /// so it is carried as an entry with an error rather than thrown out of the store.
    /// </summary>
    private static DatabricksDocumentEntry Parse(string path, DatabricksDocument document)
    {
        try
        {
            var root = DatabricksYaml.Root(document);
            return new DatabricksDocumentEntry(
                document,
                BundleParser.Parse(root, document),
                JobParser.Parse(root, document),
                PipelineParser.Parse(root, document),
                "",
                0);
        }
        catch (YamlException exception)
        {
            var line = (int)exception.Start.Line;
            _logger.Debug(exception, "{Path} does not parse at line {Line}", path, line);
            return new DatabricksDocumentEntry(
                document,
                BundleModel.Empty,
                [],
                [],
                exception.Message,
                Math.Max(line, 1));
        }
    }
}
