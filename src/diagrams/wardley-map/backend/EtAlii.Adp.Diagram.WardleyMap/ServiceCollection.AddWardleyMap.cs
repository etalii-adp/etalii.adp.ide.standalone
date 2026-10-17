using EtAlii.Adp.Context;
using EtAlii.Adp.History;
using EtAlii.Adp.Sessions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EtAlii.Adp.Diagram.WardleyMap;

/// <summary>
/// Registers the Wardley map module against core's seams. One call for the module, as
/// <c>AddC4</c> and <c>AddMindmap</c> are for theirs - the host names the module once rather
/// than listing its seams, and a test that needs the real module calls the same method. Core
/// never names the module back: everything resolves by <see cref="ToolOrigin"/>
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
        // Written from the file's name, which titles the new map.
        services.AddEmptyDocument(Diagram.WardleyMap.Origin, EmptyDocument);

        // Model to core elements and deltas, including the axis flip.
        services.TryAddSingleton<WardleyElementMapper>();

        // The per-connection view: baseline, viewport, deltas and a positional drag
        // (Requirement 10.8).
        services.AddDiagramSession(Diagram.WardleyMap.Origin, (provider, opening) => new WardleySession(
            opening.BodyPath,
            provider.GetRequiredService<IWardleyDocumentStore>(),
            provider.GetRequiredService<WardleyElementMapper>(),
            provider.GetRequiredService<IHistoryStackStore>().Get(opening.RootPath)));

        // The reload seam: an external write to a map reaches the store, and through it every
        // open session (modular-text-editors Requirement 5.3).
        services.AddDiagramDocumentReloader<IWardleyDocumentStore>(Diagram.WardleyMap.Origin);

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

    /// <summary>
    /// The empty map a new diagram starts as: a `title` line carrying the file's base name, and
    /// nothing else (Requirement 1.4). A map with no components is a valid map - it opens showing
    /// its two axes and the four evolution bands, which is exactly what an author starts from.
    /// </summary>
    /// <remarks>
    /// What this emits parses cleanly through the real OnlineWardleyMaps parser, which is the bar
    /// the task 2 corpus set for every `.owm` this module writes.
    /// </remarks>
    public static string EmptyDocument(string baseName)
    {
        ArgumentNullException.ThrowIfNull(baseName);

        // LF and a trailing newline: the writer preserves whatever a file already uses
        // (Requirement 3.1), but a file ADP creates has no existing style to preserve, and LF
        // is what every other tool in this ecosystem writes.
        return $"title {baseName}\n";
    }
}
