using EtAlii.Adp.Backend;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EtAlii.Adp.Diagram.WardleyMap;

/// <summary>
/// Registers the module's command handlers and the document store they act on
/// (Requirement 9.7). The module owns this rather than adding its handlers to core's
/// <c>AddCommands</c>, which would have core reference this assembly - the dependency direction
/// Requirement 12.4 forbids.
/// </summary>
/// <remarks>
/// Separate from <see cref="ServiceCollectionAddWardleyMapExtension.AddWardleyMap"/> - which
/// calls it - so that a test needing only the edits can have them without a session factory, a
/// toolbox and a validator it will not use. The mindmap module is split the same way, for the
/// same kind of consumer; c4, azure-pipeline and ansible-structure have never needed the
/// narrower surface, so they keep one file (see tech.md's registration rule). Requirement 9.7
/// names this method.
/// </remarks>
public static class ServiceCollectionAddWardleyMapCommandsExtension
{
    public static IServiceCollection AddWardleyMapCommands(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // One document per path, shared by every connection viewing it. TryAdd rather than Add
        // so a test that registers its own store keeps it, and so calling this twice cannot
        // quietly produce two stores that drift apart (Requirement 10.6).
        services.TryAddSingleton<IWardleyDocumentStore, WardleyDocumentStore>();

        // Where the identities the `.owm` format does not provide are kept. Stateless, so one
        // instance serves every map (Requirement 4.2).
        services.TryAddSingleton<WardleyIdentities>();

        // Adding: the three statement kinds, plus the two things a map is written on.
        services.AddSingleton<ICommandHandler<AddWardleyElementCommand>, AddWardleyElementCommandHandler>();
        services.AddSingleton<ICommandHandler<AddWardleyNoteCommand>, AddWardleyNoteCommandHandler>();
        services.AddSingleton<ICommandHandler<AddWardleyAnnotationCommand>, AddWardleyAnnotationCommandHandler>();

        // Changing what an element IS.
        services.AddSingleton<ICommandHandler<RemoveWardleyElementCommand>, RemoveWardleyElementCommandHandler>();
        services.AddSingleton<ICommandHandler<RenameWardleyElementCommand>, RenameWardleyElementCommandHandler>();
        services.AddSingleton<ICommandHandler<MoveWardleyElementCommand>, MoveWardleyElementCommandHandler>();

        // Changing what is CLAIMED about it.
        services.AddSingleton<ICommandHandler<SetWardleyInertiaCommand>, SetWardleyInertiaCommandHandler>();
        services.AddSingleton<ICommandHandler<SetWardleyDecoratorCommand>, SetWardleyDecoratorCommandHandler>();
        services.AddSingleton<ICommandHandler<SetWardleyDecoratorsCommand>, SetWardleyDecoratorsCommandHandler>();
        services.AddSingleton<ICommandHandler<SetWardleyEvolveCommand>, SetWardleyEvolveCommandHandler>();

        // Changing how elements RELATE.
        services.AddSingleton<ICommandHandler<SetWardleyLinkCommand>, SetWardleyLinkCommandHandler>();
        services.AddSingleton<ICommandHandler<SetWardleyPipelineMembershipCommand>, SetWardleyPipelineMembershipCommandHandler>();

        // The two inverses. An edit that touches one line reports the first; one that touches
        // several - an add, a remove, a rename - reports the second, because putting the
        // document back is the only description of those that is both exact and simple.
        services.AddSingleton<ICommandHandler<RestoreWardleyLineCommand>, RestoreWardleyLineCommandHandler>();
        services.AddSingleton<ICommandHandler<RestoreWardleyDocumentCommand>, RestoreWardleyDocumentCommandHandler>();

        return services;
    }
}
