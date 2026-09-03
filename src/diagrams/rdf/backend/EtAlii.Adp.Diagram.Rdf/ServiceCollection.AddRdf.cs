using EtAlii.Adp.Backend;
using EtAlii.Adp.Backend.Diagrams;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EtAlii.Adp.Diagram.Rdf;

/// <summary>
/// Registers the RDF family against core's seams.
/// </summary>
/// <remarks>
/// One call for the module, as its siblings have - the host names the module once rather than
/// listing its seams. The sibling readings joining this family later register their own
/// projections and providers beside these; the store, the commands and the data-graph validator
/// are the family's and registered here once.
/// </remarks>
public static class ServiceCollectionAddRdfExtension
{
    /// <summary>The family's anchor origin, as docs/diagrams.md writes it.</summary>
    public static readonly DiagramOrigin RdfOrigin = new("w3c", "rdf");

    public static IServiceCollection AddRdf(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // One store for the process: every reading of a file shares its document, or an edit
        // through one would be invisible to the others. TryAdd, so a test that registered its
        // own first keeps it.
        services.TryAddSingleton<IRdfDocumentStore, RdfDocumentStore>();

        // The family's commands, so every real edit is one undo away (tech.md's Commands rule).
        services.AddSingleton<ICommandHandler<AddRdfTripleCommand>, AddRdfTripleCommandHandler>();
        services.AddSingleton<ICommandHandler<RemoveRdfTripleCommand>, RemoveRdfTripleCommandHandler>();
        services.AddSingleton<ICommandHandler<RemoveRdfResourceCommand>, RemoveRdfResourceCommandHandler>();
        services.AddSingleton<ICommandHandler<RenameRdfTermCommand>, RenameRdfTermCommandHandler>();
        services.AddSingleton<ICommandHandler<ReplaceRdfObjectLiteralCommand>, ReplaceRdfObjectLiteralCommandHandler>();
        services.AddSingleton<ICommandHandler<AddRdfPrefixCommand>, AddRdfPrefixCommandHandler>();
        services.AddSingleton<ICommandHandler<RestoreRdfDocumentCommand>, RestoreRdfDocumentCommandHandler>();

        // The rules, resolved by origin through core's validator registry, so the family's
        // problems reach the Errors and Warnings panel like any other type's (Requirement 7).
        services.AddSingleton<IDiagramValidator>(_ => new RdfValidator(RdfOrigin));

        // One mapper for the family; the projections it renders differ per reading.
        services.TryAddSingleton<RdfElementMapper>();

        // The session seam, the empty body a new diagram starts as, and the reload seam - the
        // arranged-module trio every sibling reading registers its own instances of.
        services.AddSingleton<IDiagramSessionFactory>(provider => new RdfSessionFactory(
            RdfOrigin,
            provider.GetRequiredService<IRdfDocumentStore>(),
            provider.GetRequiredService<RdfElementMapper>(),
            provider.GetRequiredService<IHistoryStackStore>()));
        services.AddSingleton<IDiagramDocumentFactory>(_ => new RdfDocumentFactory(RdfOrigin));
        services.AddSingleton<IDiagramDocumentReloader>(provider => new RdfDocumentReloader(
            RdfOrigin,
            provider.GetRequiredService<IRdfDocumentStore>()));

        return services;
    }
}
