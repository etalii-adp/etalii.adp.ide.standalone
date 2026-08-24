using EtAlii.Adp.Backend.Authentication;
using EtAlii.Adp.Backend.Client;
using EtAlii.Adp.Backend.Context;
using EtAlii.Adp.Backend.Hierarchy;
using EtAlii.Adp.Backend;
using EtAlii.Adp.Backend.Projects;
using EtAlii.Adp.Backend.Sessions;
using EtAlii.Adp.Diagram;
using EtAlii.Adp.Backend.Diagrams;
using EtAlii.Adp.Diagram.Mindmap;
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
// Built by hand: the provider's other constructor takes the diagram definitions a test hands
// it, and letting the container choose between the two would leave which one it picks to
// depend on what else happens to be registered.
builder.Services.AddSingleton<IContextActionProvider>(services =>
    new AddDiagramContextActionProvider(
        services.GetRequiredService<IHistoryStackStore>(),
        services.GetRequiredService<DiagramDocumentFactories>()));

// Every IDiagramDocumentFactory a module registers, looked up by origin by the create-file
// command. A module that keeps its body in a sibling file is one registration line here.
builder.Services.AddSingleton<DiagramDocumentFactories>();
// Which type a file on disk belongs to - by its .adp first line, or by a declared extension
// for a body dropped in without one. The catalog it reads is registered by AddCommands.
builder.Services.AddSingleton<DiagramFileRouter>();

// The core diagram stream: resolves a diagram's type from its .adp file and hands it to the
// module's session. Type-agnostic; a module registers an IDiagramSessionFactory to be openable.
builder.Services.AddSingleton<DiagramSessionFactories>();
builder.Services.AddSingleton<IDiagramViewportRegistry, DiagramViewportRegistry>();

builder.Services.AddSingleton<IContextSelectionStore, ContextSelectionStore>();
builder.Services.AddSingleton<ContextSelectionResolver>();
// Registered through IContextSourceResolver for the same reason as the provider above:
// a later module that makes a new kind of thing selectable (a diagram element, a
// location in a file) is one more line here and no change anywhere else.
builder.Services.AddSingleton<IContextSourceResolver, HierarchyContextSourceResolver>();

// The dispatcher, the history and every command handler. Every state change the context
// actions above make travels through here, which is what makes it undoable.
builder.Services.AddCommands();

// The Problems area: validation, the per-project problem cache, the broadcaster and the
// startup pass that reconciles the cache after a restart (errors-and-warnings-panel).
builder.Services.AddProblems(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));

// Undo and redo, offered as project-scope context actions, and the broadcaster that pushes
// their availability whenever a project's history changes (diagram-undo-redo).
builder.Services.AddSingleton<IContextActionProvider, HistoryContextActionProvider>();
builder.Services.AddSingleton<HistoryActionsBroadcaster>();

// The mindmap module: its commands and document store, its document factory, the resolver
// that makes a node selectable and the provider that offers what can be done to one. Four
// seams, one line each, and nothing in core names the module (mindmap-diagram Requirement 13).
builder.Services.AddMindmapCommands();
builder.Services.AddSingleton<MindmapViewState>();
builder.Services.AddSingleton<IDiagramDocumentFactory, MindmapDocumentFactory>();
builder.Services.AddSingleton<IContextSourceResolver, MindmapContextSourceResolver>();
builder.Services.AddSingleton<IContextActionProvider, MindmapContextActionProvider>();
builder.Services.AddSingleton<IDiagramToolboxProvider, MindmapToolboxProvider>();
// The module's layout numbers, with what appsettings.json's Mindmap section says applied -
// the minimum gap between elements as a fraction of a node's width, above all.
var mindmapOptions = builder.Configuration.GetSection(MindmapOptions.SectionName).Get<MindmapOptions>() ?? new MindmapOptions();
builder.Services.AddSingleton<MindmapElementMapper>(_ => new MindmapElementMapper(mindmapOptions.ToMetrics()));
builder.Services.AddSingleton<IDiagramSessionFactory, MindmapSessionFactory>();

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

// A type that keeps its body in a sibling file needs a factory to write that body. Checked
// here, once, so a module deployed without its factory is a startup error naming the type
// rather than a failed Add the first time a user picks it (mindmap-diagram Requirement 2.2).
var missingFactories = app.Services.GetRequiredService<DiagramDocumentFactories>().Verify(DiagramDefinition.All);
if (missingFactories.Count > 0)
{
    throw new InvalidOperationException(
        "These diagram types declare a document extension but registered no IDiagramDocumentFactory: " +
        string.Join(", ", missingFactories.Select(definition => $"{definition.Origin} ({definition.Extension})")));
}

// Two types claiming one extension is not fatal - registration files still route by their
// MIME line - but a body dropped in on its own cannot be routed, so say so once, here.
var ambiguousExtensions = app.Services.GetRequiredService<DiagramFileRouter>().AmbiguousExtensions();
if (ambiguousExtensions.Count > 0)
{
    Log.ForContext<DiagramFileRouter>().Warning(
        "More than one diagram type claims {Extensions}; files with these extensions open only through their .adp registration",
        ambiguousExtensions);
}

// Resolved eagerly so it subscribes to the history store now, at startup, rather than on the
// first request that happens to touch it - a lazily-created broadcaster would miss changes.
_ = app.Services.GetRequiredService<HistoryActionsBroadcaster>();
// Same reason: the problem broadcaster must be subscribed to the problem store before the
// first validation writes into it (errors-and-warnings-panel Requirement 1.1).
_ = app.Services.GetRequiredService<EtAlii.Adp.Backend.Problems.ProblemBroadcaster>();

// One summary line per HTTP request - method, path, status, elapsed - instead of the several
// ASP.NET Core writes by default. The routine ones are filtered out by the Serilog.AspNetCore
// override in appsettings.json: a client polling over gRPC-Web buries everything else under
// them. A request that throws is logged at Error and still comes through; drop that override
// to Information to watch them all again.
app.UseSerilogRequestLogging();

// DefaultEnabled so every mapped gRPC service (including DiagramService once that spec
// implements it) accepts grpc-web without needing an explicit .EnableGrpcWeb() call.
app.UseGrpcWeb(new GrpcWebOptions { DefaultEnabled = true });

app.MapGrpcService<AuthenticationServiceImpl>();
app.MapGrpcService<ProjectServiceImpl>();
app.MapGrpcService<HierarchyServiceImpl>();
app.MapGrpcService<ContextServiceImpl>();
app.MapGrpcService<DiagramServiceImpl>();

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
