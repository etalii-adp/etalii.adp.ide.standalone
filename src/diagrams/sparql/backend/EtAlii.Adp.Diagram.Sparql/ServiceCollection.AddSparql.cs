using EtAlii.Adp.Common;
using EtAlii.Adp.Diagram;
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
/// document factory core requires of any type declaring an extension, which supplies a starter
/// query for a file being created and never touches one that exists.
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

        // The starter body a new query file is created with. Registered because core refuses to
        // start a host whose type declares an extension without one - see SparqlDocumentFactory,
        // which records why this exists against the design's stated intent.
        services.AddSingleton<IDiagramDocumentFactory>(_ => new SparqlDocumentFactory(SparqlOrigin));

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
