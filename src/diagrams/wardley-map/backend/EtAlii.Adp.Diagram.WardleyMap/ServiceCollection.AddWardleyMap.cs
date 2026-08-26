using EtAlii.Adp.Backend;
using EtAlii.Adp.Backend.Context;
using EtAlii.Adp.Backend.Diagrams;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EtAlii.Adp.Diagram.WardleyMap;

/// <summary>
/// Registers the Wardley map module against core's seams. One call for the module, as
/// <c>AddC4</c> and <c>AddMindmap</c> are for theirs - the host names the module once rather
/// than listing its seams, and a test that needs the real module calls the same method. Core
/// never names the module back: everything resolves by <see cref="DiagramOrigin"/>
/// (Requirement 12.3).
/// </summary>
/// <remarks>
/// This currently registers one seam of the eight Requirement 12.3 lists. The document factory
/// is here first because it is the one seam that is **mandatory the moment the type declares
/// an extension**: <c>Program.cs</c> fails startup for a type that declares one without a
/// factory, so task 3 could not land without it. The session factory, resolver, action
/// provider, toolbox provider, validator, property provider and command handlers join it as
/// their own tasks land.
/// </remarks>
public static class ServiceCollectionAddWardleyMapExtension
{
    public static IServiceCollection AddWardleyMap(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Writes the empty `.owm` body the shared create-file command puts beside a new `.adp`
        // (Requirement 1.6). Resolved by origin; core never learns what a Wardley map is.
        services.AddSingleton<IDiagramDocumentFactory, WardleyDocumentFactory>();

        // One document per path, shared by every connection viewing it. TryAdd rather than Add
        // so a test that registers its own store keeps it, and so a second call to this method
        // cannot quietly produce two stores that drift apart (Requirement 10.6).
        services.TryAddSingleton<IWardleyDocumentStore, WardleyDocumentStore>();

        // Where the identities the `.owm` format does not provide are kept. Stateless, so one
        // instance serves every map (Requirement 4.2).
        services.TryAddSingleton<WardleyIdentities>();

        // Model to core elements and deltas, including the axis flip.
        services.TryAddSingleton<WardleyElementMapper>();

        // Makes an element selectable, and self-describing once it is (Requirement 11.2).
        services.AddSingleton<IContextSourceResolver, WardleyContextSourceResolver>();

        // The per-connection view: baseline, viewport, deltas and a positional drag
        // (Requirement 10.8).
        services.AddSingleton<IDiagramSessionFactory, WardleySessionFactory>();

        // The type's commands, so every edit is one undo away (tech.md's Commands rule).
        services.AddSingleton<ICommandHandler<MoveWardleyElementCommand>, MoveWardleyElementCommandHandler>();
        services.AddSingleton<ICommandHandler<AddWardleyElementCommand>, AddWardleyElementCommandHandler>();
        services.AddSingleton<ICommandHandler<RemoveWardleyElementCommand>, RemoveWardleyElementCommandHandler>();
        services.AddSingleton<ICommandHandler<RenameWardleyElementCommand>, RenameWardleyElementCommandHandler>();
        services.AddSingleton<ICommandHandler<SetWardleyInertiaCommand>, SetWardleyInertiaCommandHandler>();
        services.AddSingleton<ICommandHandler<SetWardleyDecoratorCommand>, SetWardleyDecoratorCommandHandler>();

        // The two inverses. An edit that touches one line reports the first; one that touches
        // several - an add, a remove, a rename - reports the second, because putting the
        // document back is the only description of those that is both exact and simple.
        services.AddSingleton<ICommandHandler<RestoreWardleyLineCommand>, RestoreWardleyLineCommandHandler>();
        services.AddSingleton<ICommandHandler<RestoreWardleyDocumentCommand>, RestoreWardleyDocumentCommandHandler>();

        return services;
    }
}
