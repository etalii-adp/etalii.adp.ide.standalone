using EtAlii.Adp.Common;
using Microsoft.Extensions.DependencyInjection;

namespace EtAlii.Adp.History;

/// <summary>
/// Registers undo and redo as project-scope context actions (diagram-undo-redo Requirement 5).
/// The broadcaster that pushes their availability is Context's own machinery and is
/// registered by AddContext (backend-project-decomposition task 10).
/// </summary>
/// <remarks>
/// Separate from <see cref="ServiceCollectionAddCommandsExtension.AddCommands"/> on purpose:
/// that one registers the pipeline that makes changes undoable, this one registers what
/// offers undo to a user. A host could have the first without the second.
/// </remarks>
public static class ServiceCollectionAddHistoryActionsExtension
{
    public static IServiceCollection AddHistoryActions(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IContextActionProvider, HistoryContextActionProvider>();

        return services;
    }
}
