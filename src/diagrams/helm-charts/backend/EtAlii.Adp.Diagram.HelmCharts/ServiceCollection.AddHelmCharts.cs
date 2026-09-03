using EtAlii.Adp.Backend.Diagrams;

using Microsoft.Extensions.DependencyInjection;

namespace EtAlii.Adp.Diagram.HelmCharts;

/// <summary>
/// Registers the helm-charts module against core's seams: the store that reads and watches a
/// chart root, the mapper that projects it, and - as the later groups land - the session
/// factory that streams it, the validator that judges it and the context providers that
/// describe a selection.
/// </summary>
/// <remarks>
/// <para>
/// <b>Read the list for what is not in it</b>, exactly as the first folder-subject module
/// established: no <c>IDiagramDocumentFactory</c> (no extension, so no empty body to write -
/// core writes the bare registration, and creating charts is <c>helm create</c>'s job); no
/// toolbox provider (nothing can be dragged onto a diagram that mirrors a folder); no action
/// provider and no module commands (the one edit, a reposition, dispatches the CORE layout
/// command - tech.md's every-change-is-a-command rule holds through core's own pair). What is
/// not offered is the answer (Requirement 9).
/// </para>
/// </remarks>
public static class ServiceCollectionAddHelmChartsExtension
{
    public static IServiceCollection AddHelmCharts(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // One store per host: it holds one chart per registered root and owns the watcher
        // that keeps each of them true.
        services.AddSingleton<HelmChartReader>();
        services.AddSingleton<HelmChartStore>();
        services.AddSingleton<IHelmChartStore>(provider => provider.GetRequiredService<HelmChartStore>());

        services.AddSingleton<HelmElementMapper>();

        // One session per open diagram: whole chart at Baseline, deltas from the watcher, the
        // one edit (a reposition) dispatched to core's layout command through the history.
        services.AddSingleton<IDiagramSessionFactory, HelmSessionFactory>();

        // Read-only does not mean silent: the structural mistakes this type can see are exactly
        // the ones that bite at deploy time (Requirement 10). The validator reads the folder
        // directly rather than through the store - validation is a one-shot question, and
        // routing it through the store would leave a watcher behind on every "Validate all".
        services.AddSingleton<IDiagramValidator, HelmValidator>();

        return services;
    }
}
