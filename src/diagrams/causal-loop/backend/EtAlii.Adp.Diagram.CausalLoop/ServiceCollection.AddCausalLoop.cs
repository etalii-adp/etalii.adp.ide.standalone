using EtAlii.Adp.Backend;
using EtAlii.Adp.Backend.Diagrams;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EtAlii.Adp.Diagram.CausalLoop;

/// <summary>
/// Registers the causal loop module against core's seams.
/// </summary>
/// <remarks>
/// It grows by group. The document store, the session factory, the reload seam, the validator and
/// the document factory are registered; the writers and context seams come with the editing group,
/// and the self-organizing layout with its own. The factory is the one that could never wait -
/// without it the host does not start at all.
/// </remarks>
public static class ServiceCollectionAddCausalLoopExtension
{
    /// <summary>
    /// The origin, as docs/diagrams.md writes it: <c>causal</c>, and with the <c>-diagram</c>
    /// suffix, both deliberate and both confirmed. See <see cref="Diagram"/>.
    /// </summary>
    public static readonly DiagramOrigin CausalLoopOrigin = new("systems", "causal-loop-diagram");

    public static IServiceCollection AddCausalLoop(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // One store for the process, so two connections on one document share its parse.
        // TryAdd, so a test that registered its own first keeps it.
        services.TryAddSingleton<ICausalLoopDocumentStore, CausalLoopDocumentStore>();
        services.TryAddSingleton<CausalLoopElementMapper>();

        services.AddSingleton<IDiagramSessionFactory>(provider => new CausalLoopSessionFactory(
            CausalLoopOrigin,
            provider.GetRequiredService<ICausalLoopDocumentStore>(),
            provider.GetRequiredService<CausalLoopElementMapper>(),
            provider.GetRequiredService<IHistoryStackStore>()));

        // The reload seam: a .cld changed by a text editor or a branch switch comes back
        // through here rather than being missed until the next open.
        services.AddSingleton<IDiagramDocumentReloader>(provider => new CausalLoopDocumentReloader(
            CausalLoopOrigin,
            provider.GetRequiredService<ICausalLoopDocumentStore>()));

        // The starter body a new .cld is created with. Registered because core refuses to start
        // a host whose type declares an extension without one.
        services.AddSingleton<IDiagramDocumentFactory>(_ => new CausalLoopDocumentFactory(CausalLoopOrigin));

        // The rules, resolved by origin through core's validator registry. This reading only
        // ever reports: the disagreement between a stated label and the arithmetic is a finding,
        // never a correction.
        services.AddSingleton<IDiagramValidator>(_ => new CausalLoopValidator(CausalLoopOrigin));

        return services;
    }
}
