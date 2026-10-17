using EtAlii.Adp.History;
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
    /// <summary>The shapes reading's origin, as docs/tools.md writes it.</summary>
    public static readonly ToolOrigin ShaclOrigin = new("w3c", "shacl");

    public static void AddShacl(this IServiceCollection services)
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
        services.AddEmptyDocument(ShaclOrigin, EmptyDocument);

        // The palette; origin-scoped by construction, unlike the context menu.
        services.AddSingleton<IDiagramToolboxProvider>(_ => new ShaclToolboxProvider(ShaclOrigin));

        services.AddRdfReading(ShaclOrigin, (provider, _) => new ShaclReading(provider.GetRequiredService<ShaclElementMapper>()));
    }

    /// <summary>
    /// The minimal document a new shapes diagram starts as: one node shape with a class target and
    /// one property row, parsing and validating clean, so a fresh shapes file opens as a card with
    /// something on it rather than as an empty canvas.
    /// </summary>
    /// <remarks>
    /// The starter targets a class the file does not describe, which is the normal case for this
    /// medium and not a finding - a shapes graph aims at data held elsewhere (Requirement 4.3). The
    /// chip on the new card says so, which makes the first thing a user sees also the thing this
    /// reading most needs them to understand.
    /// </remarks>
    private static string EmptyDocument(string baseName)
    {
        ArgumentNullException.ThrowIfNull(baseName);

        var local = RdfLocalName.Of(baseName);
        return "@prefix sh: <http://www.w3.org/ns/shacl#> .\r\n"
            + "@prefix xsd: <http://www.w3.org/2001/XMLSchema#> .\r\n"
            + "@prefix ex: <http://example.org/> .\r\n"
            + "\r\n"
            + $"ex:{local}Shape a sh:NodeShape ;\r\n"
            + $"    sh:targetClass ex:{local} ;\r\n"
            + $"    sh:name \"{baseName}\" ;\r\n"
            + "    sh:property [\r\n"
            + "        sh:path ex:name ;\r\n"
            + "        sh:datatype xsd:string ;\r\n"
            + "        sh:minCount 1 ;\r\n"
            + "        sh:maxCount 1 ;\r\n"
            + "    ] .\r\n";
    }
}
