using EtAlii.Adp.Common;
using EtAlii.Adp.Context;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EtAlii.Adp.Diagram.Mindmap;

/// <summary>
/// Registers the whole mindmap module against core's seams: its commands and document store,
/// the factory that writes an empty <c>.mm</c>, the resolver that makes a node selectable,
/// the provider that offers what can be done to one, the toolbox it contributes, and the
/// session that streams it.
/// </summary>
/// <remarks>
/// One call for the module, as <c>AddC4</c> is for the C4 family - the host names the module
/// once rather than listing its seams, and a test that needs the real module calls the same
/// method. Core never names the module back: everything resolves by
/// <see cref="DiagramOrigin"/> (mindmap-diagram Requirement 13).
/// </remarks>
public static class ServiceCollectionAddMindmapExtension
{
    public static IServiceCollection AddMindmap(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddMindmapCommands();
        services.AddSingleton<MindmapViewState>();
        services.AddSingleton<IDiagramDocumentFactory, MindmapDocumentFactory>();
        services.AddSingleton<IContextSourceResolver, MindmapContextSourceResolver>();
        services.AddSingleton<IContextActionProvider, MindmapContextActionProvider>();
        services.AddSingleton<IContextPropertyProvider, MindmapContextPropertyProvider>();
        services.AddSingleton<IDiagramToolboxProvider, MindmapToolboxProvider>();

        // The module's layout numbers, with what appsettings.json's Mindmap section says
        // applied - the minimum gap between elements as a fraction of a node's width, above
        // all. Read once here rather than per resolution: the metrics never change at runtime.
        var options = configuration.GetSection(MindmapOptions.SectionName).Get<MindmapOptions>() ?? new MindmapOptions();
        services.AddSingleton(_ => new MindmapElementMapper(options.ToMetrics()));

        services.AddSingleton<IDiagramSessionFactory, MindmapSessionFactory>();

        // The reload seam: an external write to a map reaches the store, and through it every
        // open session (modular-text-editors Requirement 5.3).
        services.AddSingleton<IDiagramDocumentReloader, MindmapDocumentReloader>();

        return services;
    }
}
