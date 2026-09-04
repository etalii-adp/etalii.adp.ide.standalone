using EtAlii.Adp.Backend;
using EtAlii.Adp.Backend.Diagrams;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EtAlii.Adp.Diagram.CausalLoop;

/// <summary>
/// Registers the causal loop module against core's seams.
/// </summary>
/// <remarks>
/// Short at this point in the module's construction, and it grows by group: the parser and model
/// land next, then the session and mapper, then the writers and the context seams, then the
/// self-organizing layout as an invoked action. What is here already is the one registration
/// that cannot wait - the document factory, without which the host does not start.
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
