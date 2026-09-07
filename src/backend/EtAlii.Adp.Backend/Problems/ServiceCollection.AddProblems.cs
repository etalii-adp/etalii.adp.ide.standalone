using EtAlii.Adp.Common;
using EtAlii.Adp.Context;
using EtAlii.Adp.Hierarchy;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace EtAlii.Adp.Backend.Problems;

/// <summary>
/// Registers the whole Problems area: the validator registry, the traversal, the store with
/// its per-project cache, the broadcaster that tells every connection, the maintenance that
/// follows the filesystem, and the startup pass that reconciles the cache after a restart.
/// </summary>
/// <remarks>
/// One method rather than a list of registrations in the host, so a test that needs the
/// real pipeline wires up exactly what the host does - the same reason
/// <see cref="ServiceCollectionAddCommandsExtension.AddCommands"/> exists.
/// </remarks>
public static class ServiceCollectionAddProblemsExtension
{
    public static IServiceCollection AddProblems(this IServiceCollection services, string appDataRoot)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(appDataRoot);

        // Every IDiagramValidator a module registers, looked up by origin. A module with
        // rules is one registration line in its own extension; a type without rules is
        // simply absent here, which is legitimate (Requirement 3.3).
        services.AddSingleton<DiagramValidators>();

        services.AddSingleton<ProjectValidator>();
        services.AddSingleton<IProblemStore>(provider => new ProblemStore(
            appDataRoot,
            provider.GetRequiredService<DiagramFileRouter>(),
            provider.GetRequiredService<DiagramValidators>()));
        services.AddSingleton<ProblemBroadcaster>();
        services.AddSingleton<ProblemMaintenance>();
        services.AddSingleton<StartupRevalidation>();
        services.AddSingleton<IHostedService>(provider => provider.GetRequiredService<StartupRevalidation>());

        // Validate on a folder or a diagram, Validate all on the panel itself, and the
        // resolver that lets the panel select itself - all through the ordinary context
        // seams, so no surface learns what validation is.
        services.AddSingleton<IContextActionProvider, ValidateContextActionProvider>();
        services.AddSingleton<IContextActionProvider, ValidateAllContextActionProvider>();
        services.AddSingleton<IContextSourceResolver, ProblemsContextSourceResolver>();

        // The watch seam Context consumes - see ContextWatchHooks; registered here
        // because the implementation is this area's, however Context-shaped the interface.
        services.AddSingleton<IContextWatchHooks, ContextWatchHooks>();

        return services;
    }
}
