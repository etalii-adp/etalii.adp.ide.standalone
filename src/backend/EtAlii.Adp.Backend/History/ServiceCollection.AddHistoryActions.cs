using EtAlii.Adp.Backend.Context;

using Microsoft.Extensions.DependencyInjection;

namespace EtAlii.Adp.Backend;

/// <summary>
/// Registers undo and redo as project-scope context actions, and the broadcaster that pushes
/// their availability whenever a project's history changes (diagram-undo-redo Requirement 5).
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
        services.AddSingleton<HistoryActionsBroadcaster>();

        return services;
    }
}
