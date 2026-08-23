using EtAlii.Adp.Backend.Hierarchy;
using Microsoft.Extensions.DependencyInjection;

namespace EtAlii.Adp.Backend;

/// <summary>
/// Registers the command pipeline and every handler in it.
/// </summary>
/// <remarks>
/// One method rather than a list of registrations in the host, so a test that needs to
/// dispatch a real command wires up exactly what the host does. A new command's handler is
/// added here and nowhere else; forgetting it surfaces immediately, because
/// <see cref="CommandDispatcher"/> throws for a command it cannot find a handler for.
/// </remarks>
public static class ServiceCollectionAddCommandsExtension
{
    public static IServiceCollection AddCommands(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<ICommandDispatcher, CommandDispatcher>();

        // One process-wide history for now. Scoping it per project (or per diagram, as
        // diagram-undo-redo's requirements describe) is a later step: nothing yet exposes
        // undo/redo over the wire, so no caller can observe that the stack is shared.
        services.AddSingleton<IHistoryStack>(provider =>
            new HistoryStack(provider.GetRequiredService<ICommandDispatcher>()));

        services.AddSingleton<ICommandHandler<RenameEntryCommand>, RenameEntryCommandHandler>();
        services.AddSingleton<ICommandHandler<DeleteEntryCommand>, DeleteEntryCommandHandler>();
        services.AddSingleton<ICommandHandler<CreateDiagramFileCommand>, CreateDiagramFileCommandHandler>();

        return services;
    }
}
