using System.Collections.Concurrent;
using EtAlii.Adp.Documents;
using Serilog;
using YamlDotNet.Core;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.AzurePipeline;

/// <inheritdoc cref="IPipelineDocumentStore" />
public sealed class PipelineDocumentStore : IPipelineDocumentStore
{
    private static readonly ILogger _logger = Log.ForContext<PipelineDocumentStore>();

    private readonly ConcurrentDictionary<string, PipelineDocumentEntry> _entries = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// One template resolver per workspace, which is what makes a template shared by twenty
    /// pipelines get read once rather than twenty times - the cache lives in the resolver, so it
    /// has to outlive any one document.
    /// </summary>
    private readonly ConcurrentDictionary<string, PipelineTemplates> _templates = new(StringComparer.OrdinalIgnoreCase);

    // The paths this store is writing right now, so its own save does not bounce back through
    // Reload as an "external" change - PlainEditorSession's saving guard, per path.
    private readonly ConcurrentDictionary<string, byte> _selfWrites = new(StringComparer.OrdinalIgnoreCase);

    public event EventHandler<PipelineDocumentChangedEventArgs>? Changed;

    public PipelineDocumentEntry GetOrLoad(string rootPath, string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return _entries.GetOrAdd(path, key => Load(rootPath, key));
    }

    public string Save(string rootPath, string path, PipelineDocumentEntry entry)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        ArgumentNullException.ThrowIfNull(entry);
        if (!entry.IsUsable)
        {
            // Writing a document whose model is empty because it never parsed would replace a file
            // somebody can still fix with one this module invented.
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

        // The document's own lines are authoritative and unchanged by writing them out, but what it
        // means may have changed - so the model is rebuilt from the document rather than re-read.
        var reparsed = Parse(rootPath, path, entry.Document);
        // Also re-establishes the cache from what was just written, which is a second job this
        // line now does: where a reload evicted the entry mid-command, the cache and the file agree
        // afterwards. Do not optimise it away as a redundant reassignment - that reopens half of the
        // lost-edit window this signature closed.
        _entries[path] = reparsed;
        Changed?.Invoke(this, new PipelineDocumentChangedEventArgs(path, reparsed.Model));
        return "";
    }

    public void Touch(string rootPath, string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        Changed?.Invoke(this, new PipelineDocumentChangedEventArgs(path, GetOrLoad(rootPath, path).Model));
    }

    public void Forget(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _entries.TryRemove(path, out _);
    }

    public void Reload(string rootPath, string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (_selfWrites.ContainsKey(path))
        {
            // The change on disk is this store's own save, mid-write; Save reparses and tells
            // the sessions itself.
            return;
        }

        // Whatever changed on disk may have been a template rather than this file, and a resolver
        // that kept serving what it read before would show the pipeline as it used to be.
        _entries.TryRemove(path, out _);
        Resolver(rootPath).Forget();

        var entry = GetOrLoad(rootPath, path);
        Changed?.Invoke(this, new PipelineDocumentChangedEventArgs(path, entry.Model));
    }

    private PipelineTemplates Resolver(string rootPath) =>
        _templates.GetOrAdd(rootPath, root => new PipelineTemplates(root));

    private PipelineDocumentEntry Load(string rootPath, string path)
    {
        string text;
        try
        {
            // A file that does not exist yet is an empty document, not an error: it may have been
            // registered a moment ago, and a diagram that cannot open at all is the worse answer.
            text = File.Exists(path) ? SharedDocumentReader.ReadAllText(path) : "";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.Warning(exception, "Could not read {Path}; opening it as empty", path);
            text = "";
        }

        return Parse(rootPath, path, PipelineDocument.Parse(text));
    }

    /// <summary>
    /// The model for a document, or the reason there is none.
    /// </summary>
    /// <remarks>
    /// A file that is not YAML is an ordinary state for a file somebody is editing, so it is
    /// carried as an entry with an error rather than thrown out of the store. The line comes from
    /// the parser and is what makes the message worth reading.
    /// </remarks>
    private PipelineDocumentEntry Parse(string rootPath, string path, PipelineDocument document)
    {
        try
        {
            var model = PipelineParser.Parse(document, Resolver(rootPath), path);
            return new PipelineDocumentEntry(document, model, "", 0);
        }
        catch (YamlException exception)
        {
            var line = (int)exception.Start.Line;
            _logger.Debug(exception, "{Path} does not parse at line {Line}", path, line);
            return new PipelineDocumentEntry(
                document,
                PipelineModel.Empty,
                $"Line {line}: {exception.Message}",
                line);
        }
    }
}
