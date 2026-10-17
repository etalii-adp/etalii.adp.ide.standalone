using EtAlii.Adp.Context;
using EtAlii.Adp.Diagram.Rdf.Shacl;
using EtAlii.Adp.Hierarchy;
using EtAlii.Adp.History;
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
    /// <summary>The family's anchor origin, as docs/tools.md writes it.</summary>
    public static readonly ToolOrigin RdfOrigin = new("w3c", "rdf");

    /// <summary>The ontology reading's origin - the first sibling over the same engine.</summary>
    public static readonly ToolOrigin OwlOrigin = new("w3c", "owl");

    /// <summary>Every reading of the family, each a diagram type of its own over the one store.</summary>
    private static ToolOrigin[] FamilyOrigins =>
        [RdfOrigin, OwlOrigin, ServiceCollectionAddSkosExtension.SkosOrigin, ServiceCollectionAddShaclExtension.ShaclOrigin];

    /// <summary>The idempotency marker: every family definition carries the same Build, and the first one registers.</summary>
    private sealed class FamilyRegistered;

    /// <summary>
    /// Registers one reading at <paramref name="origin"/>: its sessions, each an
    /// <see cref="RdfFamilySession{TProjection}"/> over the family's store and the project's history,
    /// and the resolver that makes its elements selectable (code-structure FR-038, FR-032).
    /// </summary>
    /// <param name="services">The host's services.</param>
    /// <param name="origin">The reading's diagram type.</param>
    /// <param name="reading">
    /// The reading for a body opened with the registration it is given, or with none. A selection
    /// is drawn without one, so a reading that takes a header from it draws a selection in its
    /// default: the scheme reading's labels in the default language.
    /// </param>
    internal static void AddRdfReading<TProjection>(
        this IServiceCollection services,
        ToolOrigin origin,
        Func<IServiceProvider, string?, IRdfReading<TProjection>> reading)
    {
        services.AddDiagramSession(origin, (provider, opening) => new RdfFamilySession<TProjection>(
            opening.BodyPath,
            opening.RegistrationPath,
            provider.GetRequiredService<IRdfDocumentStore>(),
            reading(provider, opening.RegistrationPath),
            // The project's history, so a drag on this canvas is one undo away like every other edit.
            provider.GetRequiredService<IHistoryStackStore>().Get(opening.RootPath)));

        services.AddSingleton<IContextSourceResolver>(provider =>
        {
            var drawing = reading(provider, null);
            return new RdfContextSourceResolver(
                origin,
                provider.GetRequiredService<DiagramFileRouter>(),
                provider.GetRequiredService<IRdfDocumentStore>(),
                drawing.Drawn);
        });
    }

    public static IServiceCollection AddRdf(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Both family definitions carry this Build - whichever discovery invokes first
        // registers the engine, and the second call is a no-op rather than a duplicate.
        if (services.Any(descriptor => descriptor.ServiceType == typeof(FamilyRegistered)))
        {
            return services;
        }

        services.AddSingleton<FamilyRegistered>();

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
        services.AddSingleton<ICommandHandler<RestoreDocumentCommand<IRdfDocumentStore>>, RestoreDocumentCommandHandler<IRdfDocumentStore>>();

        // The rules, resolved by origin through core's validator registry, so the family's
        // problems reach the Errors and Warnings panel like any other type's (Requirement 7).
        services.AddSingleton<IDiagramValidator>(_ => new RdfValidator(RdfOrigin));

        // The context seams, once for the whole family: which reading a file is opened under
        // does not change what an element is (the C4 precedent). The resolver is registered with
        // each reading's session, because the shared base answers for one type's files.
        services.AddSingleton<IContextActionProvider, RdfContextActionProvider>();
        services.AddSingleton<IContextPropertyProvider, RdfContextPropertyProvider>();

        // The palette: entries name actions rather than carrying an implementation, so a drop
        // and a menu click are the same edit (Requirement 6).
        services.AddSingleton<IDiagramToolboxProvider>(_ => new RdfToolboxProvider(RdfOrigin));

        // One mapper for the family; the projections it renders differ per reading.
        services.TryAddSingleton<RdfElementMapper>();

        // The session seam and the empty body a new diagram starts as, which every sibling reading
        // registers its own instances of.
        services.AddRdfReading(RdfOrigin, (provider, _) => new RdfReading(provider.GetRequiredService<RdfElementMapper>()));
        services.AddEmptyDocument(RdfOrigin, EmptyRdfDocument);

        // The ontology reading, over the same store: its own projection, layout and refusals in
        // its session, and one validator for its origin (core allows exactly one) that delegates
        // the file-level rules to the family's RdfValidator rather than restating them
        // (owl-diagram Requirement 5.5).
        services.TryAddSingleton<OwlElementMapper>();
        services.AddSingleton<IDiagramValidator>(_ => new OwlValidator(OwlOrigin));
        services.AddSingleton<IDiagramToolboxProvider>(_ => new OwlToolboxProvider(OwlOrigin));
        services.AddRdfReading(OwlOrigin, (provider, _) => new OwlReading(provider.GetRequiredService<OwlElementMapper>()));
        services.AddEmptyDocument(OwlOrigin, EmptyOwlDocument);

        // The sibling readings ride the family's one Build (the C4 arrangement): each registers
        // its own reading seams here, over the store and commands registered above.
        services.AddSkos();
        services.AddShacl();

        // The reload seam, once per reading over the one store: a write to a body reaches every
        // session on it, whichever reading opened it (code-structure FR-028).
        foreach (var origin in FamilyOrigins)
        {
            services.AddDiagramDocumentReloader<IRdfDocumentStore>(origin);
        }

        return services;
    }

    /// <summary>
    /// The minimal document a new RDF diagram starts as - one prefix, one typed resource with a
    /// label, parsing and validating clean, because a skeleton that opens with findings would be a
    /// refusal factory.
    /// </summary>
    /// <remarks>
    /// CRLF and a trailing newline: the writers preserve whatever a file already uses, but a file
    /// ADP creates has no existing style to preserve, and CRLF is the repository's own house style.
    /// </remarks>
    public static string EmptyRdfDocument(string baseName)
    {
        ArgumentNullException.ThrowIfNull(baseName);

        var local = RdfLocalName.Of(baseName);
        return "@prefix ex: <http://example.org/> .\r\n"
            + "@prefix rdfs: <http://www.w3.org/2000/01/rdf-schema#> .\r\n"
            + "\r\n"
            + $"ex:{local} a ex:Resource ;\r\n"
            + $"    rdfs:label \"{baseName}\" .\r\n";
    }

    /// <summary>
    /// The minimal document a new ontology diagram starts as: an <c>owl:Ontology</c> header - the
    /// marker the routing arrangement suggests this reading off - and one labeled class, parsing
    /// and validating clean under both the family's and this reading's rules.
    /// </summary>
    /// <remarks>
    /// CRLF and a trailing newline, like the family's own factory: a file ADP creates has no
    /// existing style to preserve, and CRLF is the repository's house style.
    /// </remarks>
    public static string EmptyOwlDocument(string baseName)
    {
        ArgumentNullException.ThrowIfNull(baseName);

        var local = RdfLocalName.Of(baseName);
        return $"@prefix : <http://example.org/{local}#> .\r\n"
            + "@prefix owl: <http://www.w3.org/2002/07/owl#> .\r\n"
            + "@prefix rdfs: <http://www.w3.org/2000/01/rdf-schema#> .\r\n"
            + "\r\n"
            + $"<http://example.org/{local}> a owl:Ontology ;\r\n"
            + $"    rdfs:label \"{baseName}\" .\r\n"
            + "\r\n"
            + ":Thing1 a owl:Class ;\r\n"
            + "    rdfs:label \"First class\" .\r\n";
    }
}
