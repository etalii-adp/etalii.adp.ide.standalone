using EtAlii.Adp.Files;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EtAlii.Adp.History;

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

        // The one file writer: what publishes or deletes a file takes it through its constructor,
        // and waits for another save of the same path without blocking a thread (code-structure
        // FR-068). Here, beside the dispatcher, because the handlers it runs are what write; the
        // static writer's own, so a caller not yet handed it takes the same turns.
        services.TryAddSingleton(AdpFileWriter.Process);

        // The eight command/handler registrations moved to AddHierarchy with the area
        // (backend-project-decomposition task 10, the task-4 forward flag honoured):
        // a handler registers beside the code it invokes, and a test host that
        // dispatches hierarchy commands calls AddHierarchy alongside AddCommands.

        // One history per project, keyed by root path. A provider reaches its project's stack
        // through Get(target.RootPath); a connection's Watch lifetime retains and releases it,
        // and the grace after the last release runs on the one clock.
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IHistoryStackStore>(provider =>
            new HistoryStackStore(
                provider.GetRequiredService<ICommandDispatcher>(),
                grace: null,
                notices: provider.GetService<ICommandNoticeSink>(),
                time: provider.GetRequiredService<TimeProvider>()));


        return services;
    }
}
