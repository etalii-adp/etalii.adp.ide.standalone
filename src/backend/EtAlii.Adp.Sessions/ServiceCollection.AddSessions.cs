using Microsoft.Extensions.DependencyInjection;

namespace EtAlii.Adp.Sessions;

/// <summary>
/// Registers the store that holds a session once a caller has signed in.
/// </summary>
/// <remarks>
/// The <see cref="SessionInterceptor"/> also needs an <see cref="ISessionExemptions"/>, which
/// whoever hands sessions out registers: the authentication's <c>AddSignIn</c> registers both.
/// </remarks>
public static class ServiceCollectionAddSessionsExtension
{
    public static void AddSessions(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<ISessionStore, InMemorySessionStore>();
    }
}
