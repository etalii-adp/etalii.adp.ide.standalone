using EtAlii.Adp.Backend.Authentication;
using EtAlii.Adp.Backend.Client;
using EtAlii.Adp.Backend.Context;
using EtAlii.Adp.Backend.Hierarchy;
using EtAlii.Adp.Backend;
using EtAlii.Adp.Backend.Projects;
using EtAlii.Adp.Backend.Sessions;
using EtAlii.Adp.Diagram;
using JetBrains.Annotations;

var builder = WebApplication.CreateBuilder(args);

var localAuthenticationOptionsSection = builder.Configuration.GetSection(LocalAuthenticatorOptions.SectionName);
builder.Services.Configure<LocalAuthenticatorOptions>(localAuthenticationOptionsSection);
builder.Services.AddSingleton<IAuthenticator, LocalAuthenticator>();
builder.Services.AddSingleton<ISessionStore, InMemorySessionStore>();
builder.Services.AddSingleton<IProjectStore>(_ => new FileProjectStore(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)));
builder.Services.AddSingleton<IHierarchyModelStore, HierarchyModelStore>();
builder.Services.AddSingleton<IContextInteractionStore, ContextInteractionStore>();
builder.Services.AddSingleton<IContextActionResolver, ContextActionResolver>();
// Registered through IContextActionProvider so the resolver picks it up from
// IEnumerable<IContextActionProvider> - a later module contributing its own actions
// (and their shortcuts) is one more line here and no change anywhere else.
builder.Services.AddSingleton<IContextActionProvider, HierarchyContextActionProvider>();
builder.Services.AddSingleton<IContextActionProvider, AddDiagramContextActionProvider>();

builder.Services.AddSingleton<IContextSelectionStore, ContextSelectionStore>();
builder.Services.AddSingleton<ContextSelectionResolver>();
// Registered through IContextSourceResolver for the same reason as the provider above:
// a later module that makes a new kind of thing selectable (a diagram element, a
// location in a file) is one more line here and no change anywhere else.
builder.Services.AddSingleton<IContextSourceResolver, HierarchyContextSourceResolver>();

builder.Services.AddSingleton<ICommandDispatcher, CommandDispatcher>();
// One process-wide history for now. Scoping it per project (or per diagram, as
// diagram-undo-redo's requirements describe) is a later step: nothing yet exposes undo/redo
// over the wire, so there is no caller to scope it for.
builder.Services.AddSingleton<IHistoryStack>(services => new HistoryStack(services.GetRequiredService<ICommandDispatcher>()));
builder.Services.AddSingleton<ICommandHandler<RenameEntryCommand>, RenameEntryCommandHandler>();

var clientAppOptionsSection = builder.Configuration.GetSection(ClientAppOptions.SectionName);
builder.Services.Configure<ClientAppOptions>(clientAppOptionsSection);
builder.Services.AddClientAppHosting();

builder.Services.AddGrpc(options =>
{
    // Applied to every gRPC call; SessionInterceptor itself exempts
    // AuthenticationService.Login (Requirement 1.6).
    options.Interceptors.Add<SessionInterceptor>();
});

var app = builder.Build();

// Once per process: after Build so the host's logger exists, before anything can serve a
// request that reads DiagramDefinition.All. Not a DI service - it runs once and its result
// is the static cache, so there is nothing for a container to hand out. A second host in
// the same process (a test process builds one per test) finds the cache filled and the
// scan is not repeated; Initialize owns that guarantee, under a lock.
var discoveryLogger = app.Services.GetRequiredService<ILogger<DiagramDefinitionDiscovery>>();
var discoveredNow = DiagramDefinition.Initialize(() =>
    new DiagramDefinitionDiscovery(discoveryLogger)
        .Discover(DiagramDefinitionDiscovery.FindApplicationAssemblies(discoveryLogger)));
if (!discoveredNow)
{
    discoveryLogger.LogInformation(
        "Diagram types were already discovered in this process; reusing the {Count} cached definitions",
        DiagramDefinition.All.Count);
}

// DefaultEnabled so every mapped gRPC service (including DiagramService once that spec
// implements it) accepts grpc-web without needing an explicit .EnableGrpcWeb() call.
app.UseGrpcWeb(new GrpcWebOptions { DefaultEnabled = true });

app.MapGrpcService<AuthenticationServiceImpl>();
app.MapGrpcService<ProjectServiceImpl>();
app.MapGrpcService<HierarchyServiceImpl>();
app.MapGrpcService<ContextServiceImpl>();
// DiagramService (grpc-core-communication) is mapped here once that spec implements it.

app.MapClientApp();

app.Run();

// Exposes the top-level-statement Program class to EtAlii.Adp.Backend.Tests'
// WebApplicationFactory<Program>-based integration test.
[UsedImplicitly]
public partial class Program;
