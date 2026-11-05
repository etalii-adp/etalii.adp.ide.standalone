using EtAlii.Adp.Documents;

namespace EtAlii.Adp.Diagram.Sparql;

/// <summary>
/// The one owner of loaded query files, keyed by path. It is the reading half of a store and no
/// more, deliberately: there is no save member of any kind, because nothing in this module writes a
/// <c>.rq</c> - the byte-identical round trip is held by construction, and the no-writer surface
/// test pins that this stays true. Were a write attempted, two stops refuse it: the binding's
/// <c>readOnly</c> and <see cref="SparqlQueryPlugin.Plan"/>, which refuses every change.
/// </summary>
/// <remarks>
/// <para>
/// <b>It declares nothing of its own.</b> Loading, reloading, the watcher's delete, forgetting and the
/// change event are the shared store's; this stays as the distinct type the module's registrations
/// resolve.
/// </para>
/// <para>
/// <b>An entry's body is the query's text and its model the query read through the binding</b> (<see cref="SparqlBody"/>),
/// with the DISL model of its records and what that draws (<see cref="SparqlDocument"/>). A file that is not
/// there, cannot be read or does not parse opens with an empty model and says why, because this
/// module never creates or repairs query files.
/// </para>
/// </remarks>
public interface ISparqlDocumentStore : IReadableDiagramDocumentStore<string, SparqlDocument>;
