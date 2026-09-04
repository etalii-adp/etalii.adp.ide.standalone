using EtAlii.Adp.Backend;
using EtAlii.Adp.Backend.Diagrams;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EtAlii.Adp.Diagram.Rdf.Shacl;

/// <summary>
/// Registers the shapes reading against core's seams, beside the family's own registrations -
/// called from <c>AddRdf</c> so the family's one <c>Build</c> wires every reading (the C4
/// arrangement: one engine, several definitions).
/// </summary>
public static class ServiceCollectionAddShaclExtension
{
    /// <summary>The shapes reading's origin, as docs/diagrams.md writes it.</summary>
    public static readonly DiagramOrigin ShaclOrigin = new("w3c", "shacl");

    public static IServiceCollection AddShacl(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // The reading's own seams; the store, the base commands and the provider trio are the
        // family's, registered once by AddRdf.
        services.TryAddSingleton<ShaclElementMapper>();

        services.AddSingleton<ICommandHandler<AddShaclPropertyRowCommand>, AddShaclPropertyRowCommandHandler>();
        services.AddSingleton<ICommandHandler<AddShaclTargetCommand>, AddShaclTargetCommandHandler>();
        services.AddSingleton<ICommandHandler<RemoveShaclTargetCommand>, RemoveShaclTargetCommandHandler>();
        services.AddSingleton<ICommandHandler<CreateShaclNodeShapeCommand>, CreateShaclNodeShapeCommandHandler>();
        services.AddSingleton<ICommandHandler<SetShaclDeactivatedCommand>, SetShaclDeactivatedCommandHandler>();
        services.AddSingleton<ICommandHandler<SetShaclLiteralCommand>, SetShaclLiteralCommandHandler>();
        services.AddSingleton<ICommandHandler<RemoveShaclShapeCommand>, RemoveShaclShapeCommandHandler>();

        // Composition, not a second registration: core allows exactly one validator per origin,
        // so the family's text facts reach a shapes-registered file through ShaclValidator.
        services.AddSingleton<IDiagramValidator>(_ => new ShaclValidator(ShaclOrigin));

        // The starter document a new shapes registration is created with.
        services.AddSingleton<IDiagramDocumentFactory>(_ => new ShaclDocumentFactory(ShaclOrigin));

        // The palette; origin-scoped by construction, unlike the context menu.
        services.AddSingleton<IDiagramToolboxProvider>(_ => new ShaclToolboxProvider(ShaclOrigin));

        services.AddSingleton<IDiagramSessionFactory>(provider => new ShaclSessionFactory(
            ShaclOrigin,
            provider.GetRequiredService<IRdfDocumentStore>(),
            provider.GetRequiredService<ShaclElementMapper>(),
            provider.GetRequiredService<IHistoryStackStore>()));

        services.AddSingleton<IDiagramDocumentReloader>(provider => new RdfDocumentReloader(
            ShaclOrigin,
            provider.GetRequiredService<IRdfDocumentStore>()));

        return services;
    }
}
