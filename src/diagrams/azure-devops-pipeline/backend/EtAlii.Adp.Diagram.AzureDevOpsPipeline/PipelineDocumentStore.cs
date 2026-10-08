using System.Collections.Concurrent;
using EtAlii.Adp.Documents;
using Serilog;
using YamlDotNet.Core;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.AzureDevOpsPipeline;

/// <inheritdoc cref="IPipelineDocumentStore" />
/// <remarks>
/// <para>
/// <b>A thin use of the shared lifecycle (backend-centralization task 6).</b> Opening, the retries
/// before a refused read is believed, keeping the last good document through a reload that cannot
/// read (R2.4), clearing it only on the watcher's delete (R2.5) and ignoring this store's own save
/// (R2.3), and creating the folder a first save needs, are all
/// <see cref="WritableDocumentLifecycle{TDocument}"/>'s. What stays here is what is this module's: how
/// a body's text becomes an entry, the template resolvers that parse needs, the refusal to write one
/// that does not parse, and telling the sessions.
/// </para>
/// <para>
/// <b>The sessions hear about a reload only when the lifecycle installed a document.</b> One that
/// kept the last good document changed nothing a session shows.
/// </para>
/// </remarks>
public sealed class PipelineDocumentStore : IPipelineDocumentStore
{
    private static readonly ILogger _logger = Log.ForContext<PipelineDocumentStore>();

    /// <summary>
    /// One template resolver per workspace, which is what makes a template shared by twenty
    /// pipelines get read once rather than twenty times - the cache lives in the resolver, so it
    /// has to outlive any one document.
    /// </summary>
    private readonly ConcurrentDictionary<string, PipelineTemplates> _templates = new(StringComparer.OrdinalIgnoreCase);

    // The workspace root each path was last asked about. The lifecycle parses from a path and its
    // text alone, but a pipeline's parse needs its root to resolve templates, so every call records
    // the root it carries before handing the path on.
    private readonly ConcurrentDictionary<string, string> _roots = new(StringComparer.OrdinalIgnoreCase);

    // A first open that cannot read opens as empty with a warning naming the path, which is the
    // lifecycle's own default and what this store did before it (R2.2) - so no unavailable document
    // is declared here.
    private readonly WritableDocumentLifecycle<PipelineDocumentEntry> _lifecycle;

    public PipelineDocumentStore()
    {
        _lifecycle = new WritableDocumentLifecycle<PipelineDocumentEntry>(
            (path, text) => Parse(_roots[path], path, LineDocument.Parse(text)),
            entry => entry.Document.Text);
    }

    public event EventHandler<PipelineDocumentChangedEventArgs>? Changed;

    public PipelineDocumentEntry GetOrLoad(string rootPath, string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _roots[path] = rootPath;
        return _lifecycle.GetOrLoad(path);
    }

    public DocumentSaveResult Save(string rootPath, string path, PipelineDocumentEntry entry)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        ArgumentNullException.ThrowIfNull(entry);
        if (!entry.IsUsable)
        {
            // Writing a document whose model is empty because it never parsed would replace a file
            // somebody can still fix with one this module invented.
            _logger.Warning("Refusing to write {Path}: it does not parse ({Error})", path, entry.Error);
            return DocumentSaveResult.Failure($"{IoPath.GetFileName(path)} does not parse, so it was not written. {entry.Error}");
        }

        // The document's own lines are authoritative and unchanged by writing them out, but what
        // they mean may have changed - so the model is rebuilt from the document before it is saved,
        // and the lifecycle caches exactly that entry, whether or not the write lands.
        _roots[path] = rootPath;
        var reparsed = Parse(rootPath, path, entry.Document);
        var result = _lifecycle.Save(path, reparsed);
        if (result.Failed)
        {
            return result;
        }

        Changed?.Invoke(this, new PipelineDocumentChangedEventArgs(path, reparsed.Model));
        return DocumentSaveResult.Ok;
    }

    /// <summary>
    /// Tells every session on this document to re-deliver, without changing the document - for a
    /// change to how it is drawn rather than to what it says.
    /// </summary>
    public void Touch(string rootPath, string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        Changed?.Invoke(this, new PipelineDocumentChangedEventArgs(path, GetOrLoad(rootPath, path).Model));
    }

    /// <summary>Forgets a document, so the next open reads it afresh.</summary>
    public void Forget(string path) => _lifecycle.Forget(path);

    public void Reload(string rootPath, string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        // Whatever changed on disk may have been a template rather than this file, and a resolver
        // that kept serving what it read before would show the pipeline as it used to be.
        _roots[path] = rootPath;
        Resolver(rootPath).Forget();

        if (_lifecycle.Reload(path))
        {
            Changed?.Invoke(this, new PipelineDocumentChangedEventArgs(path, _lifecycle.GetOrLoad(path).Model));
        }
    }

    public void BodyDeleted(string rootPath, string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        // A body already back is re-read by the lifecycle, and what it says may rest on templates
        // that changed with it - so the resolver is dropped here as it is for a reload.
        _roots[path] = rootPath;
        Resolver(rootPath).Forget();

        if (_lifecycle.BodyDeleted(path))
        {
            Changed?.Invoke(this, new PipelineDocumentChangedEventArgs(path, _lifecycle.GetOrLoad(path).Model));
        }
    }

    private PipelineTemplates Resolver(string rootPath) =>
        _templates.GetOrAdd(rootPath, root => new PipelineTemplates(root));

    /// <summary>
    /// The model for a document, or the reason there is none.
    /// </summary>
    /// <remarks>
    /// A file that is not YAML is an ordinary state for a file somebody is editing, so it is
    /// carried as an entry with an error rather than thrown out of the store. The line comes from
    /// the parser and is what makes the message worth reading.
    /// </remarks>
    private PipelineDocumentEntry Parse(string rootPath, string path, LineDocument document)
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
