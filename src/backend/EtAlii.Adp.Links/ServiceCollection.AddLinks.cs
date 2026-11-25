using EtAlii.Adp.Files;
using EtAlii.Adp.Sessions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EtAlii.Adp.Links;

/// <summary>Registers the workspace's index of links.</summary>
public static class LinksServiceCollectionExtensions
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Adds the index of links (<see cref="ILinkIndex"/>), over the one source of file events the hierarchy and the problems
        /// share. A tool type takes part by registering an <see cref="ILinkIndexer"/>.
        /// </summary>
        public IServiceCollection AddLinks()
        {
            services.TryAddSingleton<ProjectFileEvents>();
            services.TryAddSingleton<LinkIndex>();
            services.TryAddSingleton<ILinkIndex>(provider => provider.GetRequiredService<LinkIndex>());
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IProjectSessionPart, LinkTracking>());
            return services;
        }
    }
}
