using EtAlii.Adp.Api;
using Microsoft.Azure.Functions.Worker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = new HostBuilder();
var host = builder
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureServices(services =>
    {
        services.AddAdpDbContexts();
        services.AddApplicationInsightsTelemetryWorkerService();
        services.ConfigureFunctionsApplicationInsights();

        services.AddSingleton<LinkAddChangeHandler>();
        services.AddSingleton<NodeAddChangeHandler>();
        
        services.AddSingleton<IChangeHandler, MapZoomChangeHandler>();
        services.AddSingleton<IChangeHandler, MapPositionChangeHandler>();
        services.AddSingleton<IChangeHandler, NodeAddChangeHandler>();
        services.AddSingleton<IChangeHandler, NodeRemoveChangeHandler>();
        services.AddSingleton<IChangeHandler, NodeMoveChangeHandler>();
        services.AddSingleton<IChangeHandler, NodeRenameChangeHandler>();
        services.AddSingleton<IChangeHandler, LinkAddChangeHandler>();
        services.AddSingleton<IChangeHandler, LinkRemoveChangeHandler>();
    })
    .Build();

using (var scope = host.Services.CreateScope())
{
    // Create and/or migrate the database when needed.
    var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AdpDbContext>>();
    await using var db = await factory.CreateDbContextAsync();
    await db.Database.MigrateAsync();
}

await host.RunAsync();
