using EtAlii.Adp.Diagram;
using Microsoft.Extensions.DependencyInjection;

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

        foreach (var (type, viewKind) in Types)
        {
            var origin = new DiagramOrigin("c4", type);
            services.AddSingleton<IDiagramDocumentFactory>(_ => new C4DocumentFactory(origin, viewKind));
        }

        // The seventh type registers a factory that refuses: it is registered so core's startup
        // check passes, and refuses so nobody gets a half-notation (Requirement 11.7).
        services.AddSingleton<IDiagramDocumentFactory, C4CodeDocumentFactory>();

        return services;
    }
}
