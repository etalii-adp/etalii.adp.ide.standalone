using EtAlii.Adp.Backend.Authentication;
using EtAlii.Adp.Backend.Client;
using EtAlii.Adp.Backend.Context;
using EtAlii.Adp.Backend.Hierarchy;
using EtAlii.Adp.Backend;
using EtAlii.Adp.Backend.Projects;
using EtAlii.Adp.Backend.Sessions;
using EtAlii.Adp.Diagram;
using EtAlii.Adp.Backend.Diagrams;
using EtAlii.Adp.Backend.Problems;
using EtAlii.Adp.C4;
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

// Every area registers itself through one extension method of its own, so this file names
// what the host is made of rather than listing how each part is wired. A new area is one
// more line here and a ServiceCollection.Add<Area>.cs beside the code it registers.
var appDataRoot = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

// Who is calling: the authenticator, its options, and the store a login lands in.
builder.Services.AddSessions(builder.Configuration);
// Which folders a user has opened.
builder.Services.AddProjects(appDataRoot);
// The per-connection selection and interaction stores, and the resolvers over them.
builder.Services.AddContext();
// The project's folders and files, the file-to-type router, and the hierarchy's own
// context seams - including the Add action that creates a new diagram.
builder.Services.AddHierarchy();
// The type-agnostic half of diagramming: document factories, session factories, viewports.
builder.Services.AddDiagrams();

// The dispatcher, the history and every command handler. Every state change the context
// actions above make travels through here, which is what makes it undoable.
builder.Services.AddCommands();
// Undo and redo offered to a user, and the broadcaster that pushes their availability.
builder.Services.AddHistoryActions();

// The Problems area: validation, the per-project problem cache, the broadcaster and the
// startup pass that reconciles the cache after a restart (errors-and-warnings-panel).
builder.Services.AddProblems(appDataRoot);

// The diagram modules. Each contributes its own seams and core names none of them back:
// everything resolves by DiagramOrigin (mindmap-diagram Requirement 13).
builder.Services.AddMindmap(builder.Configuration);
// Seven C4 types over one shared engine, differing only in the view each binds.
builder.Services.AddC4();

builder.Services.AddClientAppHosting(builder.Configuration);

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
_ = app.Services.GetRequiredService<ProblemBroadcaster>();

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
