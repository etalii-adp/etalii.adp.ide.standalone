using EtAlii.Adp.Documents;
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
/// <b>Not on <see cref="LifecycleDocumentStore{TBody, TModel}"/>, and not by a switch on it.</b>
/// That base is the writable store: its public surface includes a save, which this store must not
/// have. It implements the reading half of the shared store instead, so the shared entry, event,
/// reloader and registrations all serve it.
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
    private readonly DocumentLifecycle<DocumentEntry<string, SparqlQueryModel>> _lifecycle = new(static (_, text) => Parse(text), Unavailable);

    /// <inheritdoc />
    public event EventHandler<DocumentChangedEventArgs>? Changed;

    /// <inheritdoc />
    public DocumentEntry<string, SparqlQueryModel> GetOrLoad(string path) => _lifecycle.GetOrLoad(path);

    /// <inheritdoc />
    public DocumentEntry<string, SparqlQueryModel>? Get(string path) => _lifecycle.Get(path);

    /// <inheritdoc />
    public void Forget(string path) => _lifecycle.Forget(path);

    /// <inheritdoc />
    public void Reload(string path)
    {
        if (_lifecycle.Reload(path))
        {
            Changed?.Invoke(this, new DocumentChangedEventArgs(path));
        }
    }

    /// <inheritdoc />
    public void BodyDeleted(string path)
    {
        if (_lifecycle.BodyDeleted(path))
        {
            Changed?.Invoke(this, new DocumentChangedEventArgs(path));
        }
    }

    /// <summary>
    /// A query that does not parse opens as unavailable naming the reason (Requirement 1.3), with the
    /// line it stopped at among the entry's problems.
    /// </summary>
    private static DocumentEntry<string, SparqlQueryModel> Parse(string text) =>
        new(text, SparqlParser.Outcome(text));

    /// <summary>
    /// A query that is missing or cannot be read opens as a state this module names rather than as an
    /// empty query (R2.2). The lifecycle logs the warning naming the path for an unreadable one.
    /// </summary>
    private static DocumentEntry<string, SparqlQueryModel> Unavailable(string path, DocumentUnavailability unavailability, string reason)
    {
        var name = IoPath.GetFileName(path);
        var error = unavailability == DocumentUnavailability.Missing
            // This module never creates query files - queries are authored in text editors - so a
            // missing body is a state to name, not one to repair.
            ? $"{name} does not exist. Queries are authored in a text editor; this diagram draws an existing .rq file."
            : $"{name} could not be read: {reason}";
        return new DocumentEntry<string, SparqlQueryModel>("", SparqlQueryModel.Empty, error);
    }
}
