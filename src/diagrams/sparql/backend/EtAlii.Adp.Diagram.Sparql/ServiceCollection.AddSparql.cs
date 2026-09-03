using EtAlii.Adp.Backend;
using EtAlii.Adp.Backend.Diagrams;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EtAlii.Adp.Diagram.Sparql;

/// <summary>
/// Registers the SPARQL module against core's seams.
/// </summary>
/// <remarks>
/// Shorter than every sibling's registration, and that is the point: there are no commands,
/// because nothing writes the query; no document factory, because queries are authored in text
/// editors rather than created here; and no toolbox provider, because a read-only diagram that
/// offered a palette would be promising an edit it cannot make. What remains is the store, the
/// mapper, the session, the reloader, the validator and the read-only context seams.
/// </remarks>
public static class ServiceCollectionAddSparqlExtension
{
    /// <summary>The origin, as docs/diagrams.md writes it.</summary>
    public static readonly DiagramOrigin SparqlOrigin = new("w3c", "sparql");

    public static IServiceCollection AddSparql(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // One store for the process, so two connections on one query share its document.
        // TryAdd, so a test that registered its own first keeps it.
        services.TryAddSingleton<ISparqlDocumentStore, SparqlDocumentStore>();
        services.TryAddSingleton<SparqlElementMapper>();

        services.AddSingleton<IDiagramSessionFactory>(provider => new SparqlSessionFactory(
            SparqlOrigin,
            provider.GetRequiredService<ISparqlDocumentStore>(),
            provider.GetRequiredService<SparqlElementMapper>(),
            provider.GetRequiredService<IHistoryStackStore>()));

        // The reload seam: the text editor is where these files change, so this path is the
        // normal way a query diagram updates at all.
        services.AddSingleton<IDiagramDocumentReloader>(provider => new SparqlDocumentReloader(
            SparqlOrigin,
            provider.GetRequiredService<ISparqlDocumentStore>()));

        return services;
    }
}
