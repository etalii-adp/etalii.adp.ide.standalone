using EtAlii.Adp.Diagram;
using EtAlii.Adp.Sessions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EtAlii.Adp.Workspace;

/// <summary>
/// Registers the host side of the tools: the diagram abstractions (<c>AddDiagrams</c>), and the
/// services a connection calls, its connections and streams, the table streams, the editor
/// plumbing and the reload bridge (code-structure FR-054).
/// </summary>
public static class ServiceCollectionAddWorkspaceExtension
{
    public static IServiceCollection AddWorkspace(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddDiagrams();
        // The session factories of every kind of tool, and the viewport registry.
        services.AddToolSessions();

        // A tab's one stream (two-tab-connection-wedge Requirement 3.1) runs the hierarchy watch,
        // the context stream and the diagram pump in-process, so the three services it composes
        // are resolvable as ordinary services as well as mapped as gRPC ones; the hierarchy and
        // context projects register theirs, and the diagram service is this project's.
        services.AddSingleton<WorkspaceConnections>();

        // The designer family rides the same stream as its table member: the table streams that
        // are running, which the calls after an open find their session through.
        services.AddSingleton<TableStreams>();
        services.TryAddTransient<DiagramService>();

        // The watcher-to-store bridge: an external write to an open diagram's body or .adp
        // reaches the owning store's Reload, which is what carries it to every open session
        // as pushed deltas (modular-text-editors Requirements 5.3, 5.5). Modules opt in by
        // registering an IDiagramDocumentReloader per origin.
        services.AddSingleton<DiagramDocumentReloadBridge>();

        // The editor family rides the same stream: what nothing diagram-shaped claims falls
        // through to the resolved editor (modular-text-editors Requirement 5.1's order).
        services.AddSingleton<EditorAnalyzers>();

        return services;
    }
}
