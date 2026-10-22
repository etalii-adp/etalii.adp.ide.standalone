using EtAlii.Adp.History;
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
    /// <summary>The scheme reading's origin, as docs/tools.md writes it.</summary>
    public static readonly ToolOrigin SkosOrigin = new("w3c", "skos");

    public static void AddSkos(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // The reading's own seams; the store, base commands and the provider trio are the
        // family's, registered once by AddRdf - the trio delegates skos selections here.
        services.TryAddSingleton<SkosElementMapper>();
        services.AddSingleton<ICommandHandler<AddSkosConceptCommand>, AddSkosConceptCommandHandler>();
        services.AddSingleton<ICommandHandler<DisconnectSkosPairCommand>, DisconnectSkosPairCommandHandler>();
        services.AddSingleton<IDiagramToolboxProvider>(_ => new SkosToolboxProvider(SkosOrigin));
        services.AddSingleton<IDiagramValidator>(_ => new SkosValidator(SkosOrigin));
        services.AddRdfReading(SkosOrigin, (provider, registrationPath) =>
        {
            // A misplaced header yields null here and a validation finding there; the session
            // never guesses (Requirement 3.2).
            (string? language, _) = SkosRegistrationLanguage.Read(registrationPath);
            return new SkosReading(provider.GetRequiredService<SkosElementMapper>(), language ?? SkosLabels.DefaultLanguage);
        });
        services.AddEmptyDocument(SkosOrigin, EmptyDocument);
    }

    /// <summary>
    /// The minimal document a new scheme diagram starts as - one scheme, one top concept with a
    /// preferred label, parsing and validating clean (nothing above info), so a fresh vocabulary
    /// opens as a vocabulary rather than a refusal.
    /// It is the module binding's template for this reading (FBL §13), which writes the bytes the module wrote before.
    /// </summary>
    private static string EmptyDocument(string baseName) => RdfDefinitions.NewDocument(SkosOrigin, baseName);
}
