using EtAlii.Adp.Documents;
using EtAlii.Adp.Context;
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
/// <para>
/// <b>This method is the module's whole integration surface.</b> Requirement 12.3 lists eight
/// seams and every one of them is a line here: the document factory, the session factory, the
/// context source resolver, the action provider, the toolbox provider, the property provider,
/// the validator, and the command handlers - the last through
/// <c>AddWardleyMapCommands</c>, which Requirement 9.7 names.
/// </para>
/// <para>
/// Anything a reviewer wants to know about how this type plugs in is visible in one screen, and
/// anything NOT here is not part of the seam. That is what makes Requirement 12.5's audit
/// possible: if core had learned what a Wardley map is, it would not be from this file.
/// </para>
/// </remarks>
public static class ServiceCollectionAddWardleyMapExtension
{
    public static IServiceCollection AddWardleyMap(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Writes the empty `.owm` body the shared create-file command puts beside a new `.adp`
        // (Requirement 1.6). Resolved by origin; core never learns what a Wardley map is.
        services.AddSingleton<IDiagramDocumentFactory, WardleyDocumentFactory>();

        // Model to core elements and deltas, including the axis flip.
        services.TryAddSingleton<WardleyElementMapper>();

        // The per-connection view: baseline, viewport, deltas and a positional drag
        // (Requirement 10.8).
        services.AddSingleton<IDiagramSessionFactory, WardleySessionFactory>();

        // The reload seam: an external write to a map reaches the store, and through it every
        // open session (modular-text-editors Requirement 5.3).
        services.AddSingleton<IDiagramDocumentReloader, WardleyDocumentReloader>();

        // Makes an element selectable, and self-describing once it is (Requirement 11.2).
        services.AddSingleton<IContextSourceResolver, WardleyContextSourceResolver>();

        // The verbs, offered as data so the ribbon, the menu and the keyboard share one path
        // (Requirement 11.4).
        services.AddSingleton<IContextActionProvider, WardleyContextActionProvider>();

        // The palette, static per type and assertable without the panel that shows it
        // (Requirement 13.7).
        services.AddSingleton<IDiagramToolboxProvider, WardleyToolboxProvider>();

        // The values, and correcting one (Requirement 15.1).
        services.AddSingleton<IContextPropertyProvider, WardleyContextPropertyProvider>();

        // What is wrong with the map, reported where it is (Requirement 14.1).
        services.AddSingleton<IDiagramValidator, WardleyValidator>();

        // Every edit, and its inverse (Requirement 9.7).
        services.AddWardleyMapCommands();

        return services;
    }
}
