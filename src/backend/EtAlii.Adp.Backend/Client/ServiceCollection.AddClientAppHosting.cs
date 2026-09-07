using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace EtAlii.Adp.Backend.Client;

public static class ServiceCollectionAddClientAppHostingExtensions
{
    /// <summary>
    /// Registers how the client app is served: its options, and the HTTP client the dev-server
    /// proxy uses. The options binding lives here rather than in the host, so the area is
    /// registered by one call like every other.
    /// </summary>
    public static IServiceCollection AddClientAppHosting(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<ClientAppOptions>(configuration.GetSection(ClientAppOptions.SectionName));
        services.AddHttpClient(ClientDevServerProxy.HttpClientName);
        return services;
    }

    /// <summary>
    /// Registers a lowest-priority fallback (so it never preempts the mapped gRPC endpoints)
    /// that either proxies to the Vite dev server or serves the client's static build, per
    /// tech.md's Development tools section / decision log item 5.
    /// </summary>
    public static WebApplication MapClientApp(this WebApplication app)
    {
        var options = app.Services.GetRequiredService<IOptions<ClientAppOptions>>().Value;

        if (!string.IsNullOrEmpty(options.DevServerUrl))
        {
            var devServerBaseUri = new Uri(options.DevServerUrl);
            var httpClient = app.Services.GetRequiredService<IHttpClientFactory>().CreateClient(ClientDevServerProxy.HttpClientName);
            // Explicit catch-all pattern: the parameterless MapFallback overload defaults to a
            // "nonfile" pattern (skips any path with a dot in its last segment), which would skip
            // proxying most of Vite's own asset requests (main.tsx, env.mjs, etc).
            app.MapFallback("/{**path}", context => ClientDevServerProxy.ProxyAsync(context, httpClient, devServerBaseUri));
        }
        else
        {
            app.UseStaticFiles();
            app.MapFallbackToFile("index.html");
        }

        return app;
    }
}
