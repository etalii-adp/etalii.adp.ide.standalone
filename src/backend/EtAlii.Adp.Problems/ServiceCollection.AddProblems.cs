using EtAlii.Adp.Context;
using EtAlii.Adp.Diagram;
using EtAlii.Adp.Files;
using EtAlii.Adp.Hierarchy;
using EtAlii.Adp.Sessions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace EtAlii.Adp.Problems;

/// <summary>
/// Registers the whole Problems area: the validator registry, the traversal, the store with
/// its per-project cache, the broadcaster that tells every connection, the maintenance that
/// follows the filesystem, and the startup pass that reconciles the cache after a restart.
/// </summary>
/// <remarks>
/// One method rather than a list of registrations in the host, so a test that needs the
/// real pipeline wires up exactly what the host does - the same reason
/// <see href="ServiceCollectionAddCommandsExtension.AddCommands"/> exists.
/// </remarks>
public static class ServiceCollectionAddProblemsExtension
{
    public static void AddProblems(this IServiceCollection services, string appDataRoot)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(appDataRoot);

        // Every IDiagramValidator a module registers, looked up by origin. A module with
        // rules is one registration line in its own extension; a type without rules is
        // simply absent here, which is legitimate (Requirement 3.3).
        services.AddSingleton<DiagramValidators>();

        // The clock the cache writes and the settling of a burst coalesce on; the host
        // registers it, a test host may not.
        services.TryAddSingleton(TimeProvider.System);

        services.AddSingleton<ProjectValidator>();
        services.AddSingleton<IProblemStore>(provider => new ProblemStore(
            appDataRoot,
            provider.GetRequiredService<DiagramFileRouter>(),
            provider.GetRequiredService<DiagramValidators>(),
            provider.GetRequiredService<TimeProvider>()));
        // The broadcaster is started before the startup pass, as hosted services start in the
        // order they are registered: it must be subscribed to the problem store before the first
        // validation writes into it (Requirement 1.1; code-structure FR-064).
        services.AddSingleton<ProblemBroadcaster>();
        services.AddSingleton<IHostedService>(provider => provider.GetRequiredService<ProblemBroadcaster>());
        services.TryAddSingleton<ProjectFileEvents>();
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

        // The watching of a project's problems, acquired through the connection's scope.
        services.AddConnectionSessions();
        services.AddSingleton<IProjectSessionPart, ProblemTracking>();
    }
}
