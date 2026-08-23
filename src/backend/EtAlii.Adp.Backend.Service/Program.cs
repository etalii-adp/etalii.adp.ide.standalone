using EtAlii.Adp.Backend.Authentication;
using EtAlii.Adp.Backend.Client;
using EtAlii.Adp.Backend.Context;
using EtAlii.Adp.Backend.Hierarchy;
using EtAlii.Adp.Backend;
using EtAlii.Adp.Backend.Projects;
using EtAlii.Adp.Backend.Sessions;
using EtAlii.Adp.Diagram;
using JetBrains.Annotations;
using Serilog;

// A plain console logger first, so anything logged while the host is still being built - a
// configuration failure above all - lands somewhere instead of being dropped. Deliberately
// not CreateBootstrapLogger: a reloadable logger is frozen when a host is built, and the
// integration tests build several hosts in one process, which freezes it more than once.
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateLogger();

var builder = WebApplication.CreateBuilder(args);

// Replaces the logger above with the configured one, and points Log.Logger at it - which is
// what the `Log.ForContext<T>()` in each class's static field resolves to. Levels and sinks
// come from the Serilog section of appsettings.json rather than from code; ReadFrom.Services
// picks up any enricher or sink registered in DI.
builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext());

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

// Once per process: after Build so the Serilog pipeline is fully configured, before anything
// can serve a request that reads DiagramDefinition.All. Not a DI service - it runs once and its result
// is the static cache, so there is nothing for a container to hand out. A second host in
// the same process (a test process builds one per test) finds the cache filled and the
// scan is not repeated; Initialize owns that guarantee, under a lock.
var discoveredNow = DiagramDefinition.Initialize(() =>
    new DiagramDefinitionDiscovery()
        .Discover(DiagramDefinitionDiscovery.FindApplicationAssemblies()));
if (!discoveredNow)
{
    Log.ForContext<DiagramDefinitionDiscovery>().Information(
        "Diagram types were already discovered in this process; reusing the {Count} cached definitions",
        DiagramDefinition.All.Count);
}

// One summary line per HTTP request - method, path, status, elapsed - instead of the several
// ASP.NET Core writes by default. It is what makes a slow or failing call visible without
// turning framework logging up to Information across the board.
app.UseSerilogRequestLogging();

// DefaultEnabled so every mapped gRPC service (including DiagramService once that spec
// implements it) accepts grpc-web without needing an explicit .EnableGrpcWeb() call.
app.UseGrpcWeb(new GrpcWebOptions { DefaultEnabled = true });

app.MapGrpcService<AuthenticationServiceImpl>();
app.MapGrpcService<ProjectServiceImpl>();
app.MapGrpcService<HierarchyServiceImpl>();
app.MapGrpcService<ContextServiceImpl>();
// DiagramService (grpc-core-communication) is mapped here once that spec implements it.

app.MapClientApp();

// Through ForContext<Program> rather than the bare Log, so this line carries a SourceContext
// like every other one and does not read as coming from nowhere.
Log.ForContext<Program>().Information(
    "ADP is starting in the {Environment} environment with {DiagramTypeCount} diagram types",
    app.Environment.EnvironmentName,
    DiagramDefinition.All.Count);

try
{
    app.Run();
}
finally
{
    // Gives buffered sinks their chance to write before the process goes; harmless for the
    // console sink, and the reason a file sink added later will not silently lose its tail.
    Log.CloseAndFlush();
}

// Exposes the top-level-statement Program class to EtAlii.Adp.Backend.Tests'
// WebApplicationFactory<Program>-based integration test.
[UsedImplicitly]
public partial class Program;
