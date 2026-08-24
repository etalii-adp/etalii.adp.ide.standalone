using EtAlii.Adp.Diagram;
using EtAlii.Adp.Backend.Diagrams;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EtAlii.Adp.C4;

/// <summary>
/// Registers the C4 family against core's seams. One call covers all seven types, because they
/// share one engine and differ only in the view each binds - which is data here, not code
/// (c4-diagrams design, "Overview").
/// </summary>
/// <remarks>
/// The host calls this beside the other modules' registrations; a test that needs the real
/// factories calls it too. Core never names C4: everything resolves by
/// <see cref="DiagramOrigin"/>.
/// </remarks>
public static class ServiceCollectionAddC4Extension
{
    /// <summary>Each C4 diagram type, paired with the view kind it binds.</summary>
    private static readonly (string Type, C4ViewKind ViewKind)[] Types =
    [
        ("system-landscape", C4ViewKind.SystemLandscape),
        ("context", C4ViewKind.SystemContext),
        ("container", C4ViewKind.Container),
        ("component", C4ViewKind.Component),
        ("dynamic", C4ViewKind.Dynamic),
        ("deployment", C4ViewKind.Deployment),
    ];

    public static IServiceCollection AddC4(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // One store and one mapper for the whole family: several diagrams open one document,
        // so they must share the instance that holds it (Requirement 1.2).
        services.TryAddSingleton<IC4DocumentStore, C4DocumentStore>();
        services.TryAddSingleton(C4Metrics.Default);
        services.TryAddSingleton(provider => new C4ElementMapper(provider.GetRequiredService<C4Metrics>()));

        foreach (var (type, viewKind) in Types)
        {
            var origin = new DiagramOrigin("c4", type);
            services.AddSingleton<IDiagramDocumentFactory>(_ => new C4DocumentFactory(origin, viewKind));

            // The session seam: which view a session shows comes from the .adp file, so all six
            // factories are the same code under different origins.
            services.AddSingleton<IDiagramSessionFactory>(provider => new C4SessionFactory(
                origin,
                provider.GetRequiredService<IC4DocumentStore>(),
                provider.GetRequiredService<C4ElementMapper>()));

            // The type's rules, resolved by origin through core's validator registry, so C4's
            // violations reach the errors and warnings panel like any other type's
            // (errors-and-warnings-panel Requirement 3.1, c4-diagrams Requirement 10.8).
            services.AddSingleton<IDiagramValidator>(_ => new C4Validator(origin));
        }

        // The seventh type registers a factory that refuses: it is registered so core's startup
        // check passes, and refuses so nobody gets a half-notation (Requirement 11.7).
        services.AddSingleton<IDiagramDocumentFactory, C4CodeDocumentFactory>();

        return services;
    }
}
