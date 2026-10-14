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
/// <para>
/// <b>It does not derive from <see cref="LifecycleDocumentStore{TBody, TModel}"/></b>, because a
/// pipeline's parse needs the workspace root and the file's own path to resolve its templates, and
/// the base reads a body from its text alone (code-structure, spec 025: a module that cannot adopt
/// a shared type keeps its own, rather than the base growing a parameter only this module uses).
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
    private readonly WritableDocumentLifecycle<DocumentEntry<LineDocument, PipelineModel>> _lifecycle;

    public PipelineDocumentStore()
    {
        _lifecycle = new WritableDocumentLifecycle<DocumentEntry<LineDocument, PipelineModel>>(
            (path, text) => Parse(_roots[path], path, LineDocument.Parse(text)),
            entry => entry.Document.Text);
    }

    public event EventHandler<DocumentChangedEventArgs>? Changed;

    public DocumentEntry<LineDocument, PipelineModel> GetOrLoad(string rootPath, string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _roots[path] = rootPath;
        return _lifecycle.GetOrLoad(path);
    }

    /// <summary>
    /// Writes the document back and tells every session on it. Refuses while the document does not
    /// parse, so a file that is already broken is never made worse.
    /// </summary>
    /// <returns><see cref="DocumentSaveResult.Ok"/> when it was written, or a failure whose <see cref="DocumentSaveResult.Error"/> says why it was not (backend-centralization R3.1).</returns>
    /// <remarks>
    /// <b>The entry is a parameter rather than something this looked up, and that is the fix for a
    /// data-loss defect.</b> It used to call <see cref="GetOrLoad"/> itself, so it wrote whatever was
    /// in the cache at save time - and a <see cref="IReloadableDocumentStore.Reload"/> replaced that entry. A reload landing between a
    /// command's edit and its save therefore discarded the edit and REPORTED SUCCESS, which put the
    /// command's inverse on the undo stack for a change that never happened. Passing the entry makes
    /// that ordering unrepresentable: what the caller edited is what gets written.
    /// </remarks>
    public DocumentSaveResult Save(string rootPath, string path, DocumentEntry<LineDocument, PipelineModel> entry)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        ArgumentNullException.ThrowIfNull(entry);
        if (!entry.IsUsable)
        {
            // Writing a document whose model is empty because it never parsed would replace a file
            // somebody can still fix with one this module invented.
            _logger.Warning("Refusing to write {Path}: it does not parse ({Error})", path, entry.Unreadable);
            return DocumentSaveResult.Failure($"{IoPath.GetFileName(path)} does not parse, so it was not written. {entry.Unreadable}");
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

        OnChanged(path);
        return DocumentSaveResult.Ok;
    }

    /// <summary>
    /// Tells every session on this document to re-deliver, without changing the document - for a
    /// change to how it is drawn rather than to what it says.
    /// </summary>
    public void Touch(string rootPath, string path)
    {
        GetOrLoad(rootPath, path);
        OnChanged(path);
    }

    public void Forget(string path) => _lifecycle.Forget(path);

    public IDiagramDocumentStore<LineDocument, PipelineModel> In(string rootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        return new Rooted(this, rootPath);
    }

    public PipelineModel ModelOf(string rootPath, string path, LineDocument document)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(document);
        return Parse(rootPath, path, document).Model;
    }

    public void Reload(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        // Whatever changed on disk may have been a template rather than this file, and a resolver
        // that kept serving what it read before would show the pipeline as it used to be.
        ForgetTemplates();

        // A document never opened has no root to resolve its templates against, and nothing open
        // on it to tell: its first open reads it then.
        if (_roots.ContainsKey(path) && _lifecycle.Reload(path))
        {
            OnChanged(path);
        }
    }

    public void BodyDeleted(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        // A body already back is re-read by the lifecycle, and what it says may rest on templates
        // that changed with it - so the resolvers are dropped here as they are for a reload.
        ForgetTemplates();

        if (_roots.ContainsKey(path) && _lifecycle.BodyDeleted(path))
        {
            OnChanged(path);
        }
    }

    /// <summary>
    /// Drops what every workspace's resolver read. The path that changed does not say which root
    /// it lies under, and a forgotten template costs one read on the next parse.
    /// </summary>
    private void ForgetTemplates()
    {
        foreach (var resolver in _templates.Values)
        {
            resolver.Forget();
        }
    }

    private void OnChanged(string path) => Changed?.Invoke(this, new DocumentChangedEventArgs(path));

    /// <summary>The store with every open and save made from one root.</summary>
    private sealed class Rooted(PipelineDocumentStore documents, string rootPath) : IDiagramDocumentStore<LineDocument, PipelineModel>
    {
        public event EventHandler<DocumentChangedEventArgs>? Changed
        {
            add => documents.Changed += value;
            remove => documents.Changed -= value;
        }

        public DocumentEntry<LineDocument, PipelineModel> GetOrLoad(string path) => documents.GetOrLoad(rootPath, path);

        /// <remarks>Refused, as the store refuses it, while what the file holds does not parse.</remarks>
        public DocumentSaveResult Save(string path, LineDocument document)
        {
            var entry = documents.GetOrLoad(rootPath, path) with { Document = document };
            return documents.Save(rootPath, path, entry);
        }

        public void Reload(string path) => documents.Reload(path);

        public void BodyDeleted(string path) => documents.BodyDeleted(path);

        public void Forget(string path) => documents.Forget(path);
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
    private DocumentEntry<LineDocument, PipelineModel> Parse(string rootPath, string path, LineDocument document)
    {
        try
        {
            var model = PipelineParser.Parse(document, Resolver(rootPath), path);
            return new DocumentEntry<LineDocument, PipelineModel>(document, model);
        }
        catch (YamlException exception)
        {
            var line = (int)exception.Start.Line;
            _logger.Debug(exception, "{Path} does not parse at line {Line}", path, line);
            return new DocumentEntry<LineDocument, PipelineModel>(document, PipelineModel.Empty, $"Line {line}: {exception.Message}");
        }
    }
}
