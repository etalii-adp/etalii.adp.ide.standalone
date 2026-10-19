using EtAlii.Adp.Context;
using EtAlii.Adp.History;
using EtAlii.Adp.Sessions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EtAlii.Adp.Diagram.Sparql;

/// <summary>
/// Registers the SPARQL module against core's seams.
/// </summary>
/// <remarks>
/// Shorter than every sibling's registration, and that is the point: there are no commands,
/// because nothing writes the query, and no toolbox provider, because a read-only diagram that
/// offered a palette would be promising an edit it cannot make. What remains is the store, the
/// mapper, the session, the reloader, the validator, the read-only context seams - and the
/// empty document core requires of any type declaring an extension, which supplies a starter
/// query for a file being created and never touches one that exists.
/// </remarks>
public static class ServiceCollectionAddSparqlExtension
{
    /// <summary>The origin, as docs/tools.md writes it.</summary>
    public static readonly ToolOrigin SparqlOrigin = new("w3c", "sparql");

    /// <summary>The query a new SPARQL diagram starts as.</summary>
    private const string EmptyQuery = "PREFIX rdfs: <http://www.w3.org/2000/01/rdf-schema#>\r\n"
        + "\r\n"
        + "SELECT ?subject ?label\r\n"
        + "WHERE {\r\n"
        + "  ?subject rdfs:label ?label .\r\n"
        + "}\r\n"
        + "LIMIT 100\r\n";

    public static IServiceCollection AddSparql(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // One store for the process, so two connections on one query share its document.
        // TryAdd, so a test that registered its own first keeps it.
        services.TryAddSingleton<ISparqlDocumentStore, SparqlDocumentStore>();
        services.TryAddSingleton<SparqlElementMapper>();

        services.AddDiagramSession(SparqlOrigin, (provider, opening) => new SparqlSession(
            opening.BodyPath,
            opening.RegistrationPath,
            provider.GetRequiredService<ISparqlDocumentStore>(),
            provider.GetRequiredService<SparqlElementMapper>(),
            // The project's history, so the one gesture this diagram has is one undo away.
            provider.GetRequiredService<IHistoryStackStore>().Get(opening.Root)));

        // The reload seam: the text editor is where these files change, so this path is the
        // normal way a query diagram updates at all.
        services.AddDiagramDocumentReloader<ISparqlDocumentStore>(SparqlOrigin);

        // The starter query a new file is created with: a SELECT that parses and validates clean,
        // so a file ADP creates opens as a diagram rather than as a finding. Every prefix it
        // declares is used, so it reports nothing at all.
        //
        // This exists against the design's stated intent, which said this module would register no
        // new document, because queries are authored in text editors. Core disagrees at a level a
        // module cannot argue with: a type that declares an extension and registers no new document
        // is a startup error naming it (DiagramDocumentFactories.Verify), so the host would not boot.
        // The read-only position is untouched: this is the text of a file being created, never a
        // rewrite of one that exists. CRLF, the repository's house style, since a new file has no
        // style of its own to keep.
        services.AddEmptyDocument(SparqlOrigin, EmptyQuery);

        // The rules, resolved by origin through core's validator registry.
        services.AddSingleton<IDiagramValidator>(_ => new SparqlValidator(SparqlOrigin));

        // The context seams. The action provider is registered precisely so it can offer
        // nothing: an empty answer states in code that this diagram is a reading surface.
        services.AddSingleton<IContextSourceResolver, SparqlContextSourceResolver>();
        services.AddSingleton<IContextActionProvider, SparqlContextActionProvider>();
        services.AddSingleton<IContextPropertyProvider, SparqlContextPropertyProvider>();

        return services;
    }
}
