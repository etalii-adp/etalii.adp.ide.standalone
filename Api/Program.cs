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

        services.AddSingleton<ICommandHandler, MapZoomCommandHandler>();
        services.AddSingleton<ICommandHandler, MapPositionCommandHandler>();
        
        services.AddSingleton<NodeAddCommandHandler>();
        services.AddSingleton<ICommandHandler, NodeAddCommandHandler>();
        services.AddSingleton<ICommandHandler, NodeRemoveCommandHandler>();
        services.AddSingleton<ICommandHandler, NodeMoveCommandHandler>();
        services.AddSingleton<ICommandHandler, NodeRenameCommandHandler>();
        
        services.AddSingleton<LinkAddCommandHandler>();
        services.AddSingleton<ICommandHandler, LinkAddCommandHandler>();
        services.AddSingleton<ICommandHandler, LinkRemoveCommandHandler>();
        
        services.AddSingleton<TagGroupAddCommandHandler>();
        services.AddSingleton<ICommandHandler, TagGroupAddCommandHandler>();
        services.AddSingleton<ICommandHandler, TagGroupRemoveCommandHandler>();
        services.AddSingleton<ICommandHandler, TagGroupRenameCommandHandler>();
        
        services.AddSingleton<TagAddCommandHandler>();
        services.AddSingleton<ICommandHandler, TagAddCommandHandler>();
        services.AddSingleton<ICommandHandler, TagRemoveCommandHandler>();

        services.AddSingleton<TagAssignCommandHandler>();
        services.AddSingleton<ICommandHandler, TagAssignCommandHandler>();
        services.AddSingleton<ICommandHandler, TagUnassignCommandHandler>();
        
        
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
