using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EtAlii.Adp.Sessions;

/// <summary>
/// Registers the scope a connection holds its project and its own parts in, with the project's
/// history as its first part (code-structure FR-065, FR-067). Called by every area whose services
/// open a connection, so it registers once whichever calls first.
/// </summary>
public static class ServiceCollectionAddConnectionSessionsExtension
{
    public static void AddConnectionSessions(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<ConnectionSessions>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IProjectSessionPart, HistoryRetention>());
    }
}
