using EtAlii.Adp.Documents;
using Serilog;
using YamlDotNet.Core;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.Timeline;

/// <inheritdoc cref="ITimelineDocumentStore" />
/// <remarks>
/// <para>
/// <b>A thin use of the shared lifecycle (backend-centralization task 6).</b> Opening, the retries
/// before a refused read is believed, keeping the last good document through a reload that cannot
/// read (R2.4), clearing it only on the watcher's delete (R2.5) and ignoring this store's own save
/// (R2.3), and creating the folder a first save needs, are all
/// <see cref="WritableDocumentLifecycle{TDocument}"/>'s. What stays here is what is this module's: how
/// a body's text becomes an entry, the refusal to write one that does not parse, and telling the
/// sessions.
/// </para>
/// <para>
/// <b>The sessions hear about a reload only when the lifecycle installed a document.</b> One that
/// kept the last good document changed nothing a session shows.
/// </para>
/// </remarks>
public sealed class TimelineDocumentStore : ITimelineDocumentStore
{
    private static readonly ILogger _logger = Log.ForContext<TimelineDocumentStore>();

    // A first open that cannot read opens as empty with a warning naming the path, which is the
    // lifecycle's own default and what this store did before it (R2.2) - so no unavailable document
    // is declared here.
    private readonly WritableDocumentLifecycle<TimelineDocumentEntry> _lifecycle = new(
        (path, text) => Parse(path, LineDocument.Parse(text)),
        entry => entry.Document.Text);

    /// <inheritdoc />
    public event EventHandler<TimelineDocumentChangedEventArgs>? Changed;

    /// <inheritdoc />
    public TimelineDocumentEntry GetOrLoad(string path) => _lifecycle.GetOrLoad(path);

    /// <inheritdoc />
    public DocumentSaveResult Save(string path, TimelineDocumentEntry entry)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(entry);

        if (!entry.IsUsable)
        {
            // Writing a document whose model is empty because it never parsed would replace a
            // file somebody can still fix with one this module invented.
            _logger.Warning("Refusing to write {Path}: it does not parse ({Error})", path, entry.Error);
            return DocumentSaveResult.Failure($"{IoPath.GetFileName(path)} does not parse, so it was not written. {entry.Error}");
        }

        // The document's own lines are authoritative and unchanged by writing them out, but what
        // they mean has changed - so the model is rebuilt from the document before it is saved,
        // and the lifecycle caches exactly that entry, whether or not the write lands.
        var reparsed = Parse(path, entry.Document);
        var result = _lifecycle.Save(path, reparsed);
        if (result.Failed)
        {
            return result;
        }

        Changed?.Invoke(this, new TimelineDocumentChangedEventArgs(path, reparsed.Model));
        return DocumentSaveResult.Ok;
    }

    /// <summary>Forgets a document, so the next open reads it afresh.</summary>
    public void Forget(string path) => _lifecycle.Forget(path);

    /// <inheritdoc />
    public void Reload(string path)
    {
        if (_lifecycle.Reload(path))
        {
            Changed?.Invoke(this, new TimelineDocumentChangedEventArgs(path, _lifecycle.GetOrLoad(path).Model));
        }
    }

    /// <inheritdoc />
    public void BodyDeleted(string path)
    {
        if (_lifecycle.BodyDeleted(path))
        {
            Changed?.Invoke(this, new TimelineDocumentChangedEventArgs(path, _lifecycle.GetOrLoad(path).Model));
        }
    }

    /// <summary>
    /// The model for a document, or the reason there is none. A file that is not YAML is an
    /// ordinary state for a file somebody is editing, so it is carried as an entry with an error
    /// rather than thrown out of the store.
    /// </summary>
    private static TimelineDocumentEntry Parse(string path, LineDocument document)
    {
        try
        {
            return new TimelineDocumentEntry(document, TimelineParser.Parse(document), "", 0);
        }
        catch (YamlException exception)
        {
            var line = (int)exception.Start.Line;
            _logger.Debug(exception, "{Path} does not parse at line {Line}", path, line);
            return new TimelineDocumentEntry(
                document,
                TimelineModel.Empty,
                exception.Message,
                Math.Max(line, 1));
        }
    }
}
