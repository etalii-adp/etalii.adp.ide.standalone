using EtAlii.Adp.Documents;
using Serilog;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.Sparql;

/// <inheritdoc cref="ISparqlDocumentStore" />
/// <remarks>
/// <para>
/// <b>A thin use of the shared read-only lifecycle (backend-centralization task 7, R2.7).</b>
/// Opening, the retries before a refused read is believed, keeping the last good query through a
/// reload that cannot read (R2.4) and clearing it only on the watcher's delete (R2.5) are all
/// <see cref="DocumentLifecycle{TDocument}"/>'s. What stays here is what is this module's: how a
/// query's text becomes an entry, the entry a missing or unreadable query opens as (R2.2), and
/// telling the sessions.
/// </para>
/// <para>
/// <b>There is no save path, and none is borrowed.</b> This store holds the read-only lifecycle, not
/// the writable one, so there is no self-write bookkeeping here either: a store that never writes has
/// nothing to suppress, and every change on disk is somebody else's and is always taken.
/// </para>
/// <para>
/// <b>The sessions hear about a reload only when the lifecycle installed a document.</b> One that
/// kept the last good query changed nothing a session shows. Before this store used the lifecycle, a
/// reload that could not read replaced a good query with an error naming the read failure.
/// </para>
/// </remarks>
public sealed class SparqlDocumentStore : ISparqlDocumentStore
{
    private static readonly ILogger _logger = Log.ForContext<SparqlDocumentStore>();

    private readonly DocumentLifecycle<SparqlDocumentEntry> _lifecycle = new(Parse, Unavailable);

    /// <inheritdoc />
    public event EventHandler<SparqlDocumentChangedEventArgs>? Changed;

    /// <inheritdoc />
    public SparqlDocumentEntry GetOrLoad(string path) => _lifecycle.GetOrLoad(path);

    /// <inheritdoc />
    public void Forget(string path) => _lifecycle.Forget(path);

    /// <inheritdoc />
    public void Reload(string path)
    {
        if (_lifecycle.Reload(path))
        {
            Changed?.Invoke(this, new SparqlDocumentChangedEventArgs(path, _lifecycle.GetOrLoad(path)));
        }
    }

    /// <inheritdoc />
    public void BodyDeleted(string path)
    {
        if (_lifecycle.BodyDeleted(path))
        {
            Changed?.Invoke(this, new SparqlDocumentChangedEventArgs(path, _lifecycle.GetOrLoad(path)));
        }
    }

    private static SparqlDocumentEntry Parse(string path, string text)
    {
        try
        {
            return new SparqlDocumentEntry(text, SparqlParser.Parse(text), "", 0);
        }
        catch (SparqlParseException exception)
        {
            // A file that does not parse is an ordinary state for a file somebody is editing:
            // it opens as unavailable naming file, line and reason (Requirement 1.3).
            _logger.Debug(exception, "{Path} does not parse at line {Line}", path, exception.Line);
            return new SparqlDocumentEntry(text, SparqlQueryModel.Empty, exception.Message, exception.Line);
        }
    }

    /// <summary>
    /// A query that is missing or cannot be read opens as a state this module names rather than as an
    /// empty query (R2.2). The lifecycle logs the warning naming the path for an unreadable one.
    /// </summary>
    private static SparqlDocumentEntry Unavailable(string path, DocumentUnavailability unavailability, string reason)
    {
        var name = IoPath.GetFileName(path);
        var error = unavailability == DocumentUnavailability.Missing
            // This module never creates query files - queries are authored in text editors - so a
            // missing body is a state to name, not one to repair.
            ? $"{name} does not exist. Queries are authored in a text editor; this diagram draws an existing .rq file."
            : $"{name} could not be read: {reason}";
        return new SparqlDocumentEntry("", SparqlQueryModel.Empty, error, 0);
    }
}
