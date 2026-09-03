using EtAlii.Adp.Backend;
using EtAlii.Adp.Backend.Diagrams;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EtAlii.Adp.Diagram.Rdf;

/// <summary>
/// Registers the scheme reading against core's seams, beside the family's own registrations -
/// called from <see cref="ServiceCollectionAddRdfExtension.AddRdf"/> so the family's one
/// <c>Build</c> wires every reading (the C4 arrangement: one engine, several definitions).
/// </summary>
public static class ServiceCollectionAddSkosExtension
{
    /// <summary>The scheme reading's origin, as docs/diagrams.md writes it.</summary>
    public static readonly DiagramOrigin SkosOrigin = new("w3c", "skos");

    public static IServiceCollection AddSkos(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // The reading's own seams; the store, commands and context providers are the family's,
        // registered once by AddRdf.
        services.TryAddSingleton<SkosElementMapper>();
        services.AddSingleton<IDiagramValidator>(_ => new SkosValidator(SkosOrigin));
        services.AddSingleton<IDiagramSessionFactory>(provider => new SkosSessionFactory(
            SkosOrigin,
            provider.GetRequiredService<IRdfDocumentStore>(),
            provider.GetRequiredService<SkosElementMapper>(),
            provider.GetRequiredService<IHistoryStackStore>()));
        services.AddSingleton<IDiagramDocumentFactory>(_ => new SkosDocumentFactory(SkosOrigin));
        services.AddSingleton<IDiagramDocumentReloader>(provider => new RdfDocumentReloader(
            SkosOrigin,
            provider.GetRequiredService<IRdfDocumentStore>()));

        return services;
    }
}
