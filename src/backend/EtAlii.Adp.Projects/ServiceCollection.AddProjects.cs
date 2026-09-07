using Microsoft.Extensions.DependencyInjection;

namespace EtAlii.Adp.Projects;

public static class ServiceCollectionAddProjectsExtension
{
    /// <summary>
    /// Registers the project store: which folders a user has opened, kept as a file under
    /// <paramref name="appDataRoot"/> rather than in a database.
    /// </summary>
    /// <remarks>
    /// The root is a parameter rather than read from the environment here, so a test can point
    /// the store at a temporary folder without replacing the registration - the same shape
    /// <see href="EtAlii.Adp.Problems.ServiceCollectionAddProblemsExtension.AddProblems"/> uses.
    /// </remarks>
    /// <exception cref="ArgumentNullException"></exception>
    /// <exception cref="ArgumentException"></exception>
    public static IServiceCollection AddProjects(this IServiceCollection services, string appDataRoot)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(appDataRoot);

        services.AddSingleton<IProjectStore>(_ => new FileProjectStore(appDataRoot));

        return services;
    }
}
