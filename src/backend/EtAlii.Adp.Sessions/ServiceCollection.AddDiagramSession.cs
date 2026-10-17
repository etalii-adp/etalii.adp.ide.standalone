using Microsoft.Extensions.DependencyInjection;

namespace EtAlii.Adp.Sessions;

public static class ServiceCollectionAddDiagramSessionExtension
{
    /// <summary>
    /// Registers how a session on a diagram of the type at <paramref name="origin"/> is opened:
    /// <paramref name="open"/> is handed the provider and what the session is opened on.
    /// </summary>
    public static void AddDiagramSession(this IServiceCollection services, ToolOrigin origin, Func<IServiceProvider, DiagramSessionOpening, IDiagramSession> open)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(origin);
        ArgumentNullException.ThrowIfNull(open);

        services.AddSingleton<IDiagramSessionFactory>(provider => new DiagramSessionFactory(origin, provider, open));
    }
}
