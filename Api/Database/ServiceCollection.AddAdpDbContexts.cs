using Microsoft.Extensions.DependencyInjection;

namespace EtAlii.Adp.Api;

public static class ServiceCollectionAddAdpDbContextsExtension
{
    public static void AddAdpDbContexts(this IServiceCollection services)
    {
        services.AddPooledDbContextFactory<AdpDbContext>(options =>
        {
            options.UseAdpDatabase();
        });
    }
}