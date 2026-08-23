using EtAlii.Adp.Backend.Hierarchy;
using EtAlii.Adp.Diagram;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

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

        // The rename and delete handlers ask the catalog whether a file is a diagram's
        // registration file with a body sibling to carry along. TryAdd, so a test that
        // registered its own list first keeps it.
        services.TryAddSingleton<IDiagramDefinitionCatalog, DiagramDefinitionCatalog>();

        services.AddSingleton<ICommandDispatcher, CommandDispatcher>();

        // One history per project, keyed by root path. A provider reaches its project's stack
        // through Get(target.RootPath); a connection's Watch lifetime retains and releases it.
        services.AddSingleton<IHistoryStackStore>(provider =>
            new HistoryStackStore(provider.GetRequiredService<ICommandDispatcher>()));

        services.AddSingleton<ICommandHandler<RenameEntryCommand>, RenameEntryCommandHandler>();
        services.AddSingleton<ICommandHandler<DeleteEntryCommand>, DeleteEntryCommandHandler>();
        services.AddSingleton<ICommandHandler<CreateDiagramFileCommand>, CreateDiagramFileCommandHandler>();

        return services;
    }
}
