using EtAlii.Adp.Backend.Authentication;
using EtAlii.Adp.Backend.Problems;

using EtAlii.Adp.Common;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EtAlii.Adp.Backend.Sessions;

/// <summary>
/// Registers everything that answers "who is calling": the authenticator and its options,
/// and the store that holds a session once a caller has logged in.
/// </summary>
/// <remarks>
/// One method rather than a list of registrations in the host, so a test that needs a real
/// login wires up exactly what the host does - the same reason
/// <see cref="ServiceCollectionAddProblemsExtension.AddProblems"/> and
/// <see cref="ServiceCollectionAddCommandsExtension.AddCommands"/> exist.
/// <para>
/// Deliberately not named <c>AddAuthentication</c>: ASP.NET Core already puts a method of
/// that name on <see cref="IServiceCollection"/>, and a second one differing only in its
/// parameters is the kind of overload nobody should have to resolve by eye.
/// </para>
/// </remarks>
public static class ServiceCollectionAddSessionsExtension
{
    public static IServiceCollection AddSessions(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<LocalAuthenticatorOptions>(configuration.GetSection(LocalAuthenticatorOptions.SectionName));
        services.AddSingleton<IAuthenticator, LocalAuthenticator>();
        services.AddSingleton<ISessionStore, InMemorySessionStore>();

        return services;
    }
}
