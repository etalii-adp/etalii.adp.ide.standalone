using EtAlii.Adp.Authentication;
using EtAlii.Adp.Backend.Client;
using EtAlii.Adp.Context;
using EtAlii.Adp.Designer;
using EtAlii.Adp.Diagram;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Editor;
using EtAlii.Adp.Hierarchy;
using EtAlii.Adp.History;
using EtAlii.Adp.Problems;
using EtAlii.Adp.Projects;
using JetBrains.Annotations;
using Serilog;

// A plain console logger first, so anything logged while the host is still being built - a
// configuration failure above all - lands somewhere instead of being dropped. Deliberately
// not CreateBootstrapLogger: a reloadable logger is frozen when a host is built, and the
// integration tests build several hosts in one process, which freezes it more than once.
// ONLY WHEN NO HOST IS LIVE, which in production is always: the integration tests start hosts
// while others are running, and installing it unconditionally replaced a live host's pipeline
// under it - a class first used in that moment kept the bootstrap logger for the whole process.
HostLoggers.InstallBootstrapIfNoneLive(() => new LoggerConfiguration()
    .WriteTo.Console()
    .CreateLogger());

var builder = WebApplication.CreateBuilder(args);

// The configured pipeline. Levels and sinks come from the Serilog section of appsettings.json
// rather than from code; ReadFrom.Services picks up any enricher or sink registered in DI.
// preserveStaticLogger: true, so this host owns ITS logger and nothing else: without it,
// Serilog points Log.Logger at the pipeline itself and, when the host is disposed, calls
// Log.CloseAndFlush() - closing whatever pipeline is global at that moment, which in a test
// process is another host's, still running. Measured: disposing one host silenced a live one
// synchronously, with Program.cs's own CloseAndFlush removed. Log.Logger is set below instead.
builder.Host
    .UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext(), preserveStaticLogger: true);

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

builder.Services.AddClientAppHosting(builder.Configuration);

// Applied to every gRPC call; SessionInterceptor itself exempts
// AuthenticationService.Login (Requirement 1.6).
builder.Services.AddGrpc(options => options.Interceptors.Add<SessionInterceptor>());

// Once per process: after Build so the Serilog pipeline is fully configured, before anything
// can serve a request that reads DiagramDefinition.All. Not a DI service - it runs once and its result
// is the static cache, so there is nothing for a container to hand out. A second host in
// the same process (a test process builds one per test) finds the cache filled and the
// scan is not repeated; Initialize owns that guarantee, under a lock.

var diagramDefinitions = DiagramDefinitionDiscovery.Discover();
builder.AddDiagramDefinitions(diagramDefinitions);

// The editor family, discovered by the same walk and registered the same way.
var editorDefinitions = EditorDefinitionDiscovery.Discover();
builder.AddEditorDefinitions(editorDefinitions);

// The designer family, discovered by the same walk and registered the same way. An application
// without a designer module registers an empty catalog, so code that asks for it is always served.
var designerDefinitions = DesignerDefinitionDiscovery.Discover();
builder.AddDesignerDefinitions(designerDefinitions);

var app = builder.Build();

// Points Log.Logger at this host's pipeline - which is what the `Log.ForContext<T>()` in each
// class's static field resolves to - and takes it back when the host stops, handing the global to
// the newest host still running rather than closing it. Released on ApplicationStopped, before the
// host disposes its logger, and by the `using` if startup throws before the host ever runs.
var hostLogger = app.Services.GetRequiredService<Serilog.ILogger>();
using var hostLoggerInstalled = HostLoggers.Install(hostLogger);
app.Lifetime.ApplicationStopped.Register(hostLoggerInstalled.Dispose);

// A type that keeps its body in a sibling file needs a factory to write that body. Checked
// here, once, so a module deployed without its factory is a startup error naming the type
// rather than a failed Add the first time a user picks it (mindmap-diagram Requirement 2.2).
var missingFactories = app.Services.GetRequiredService<DiagramDocumentFactories>().Verify(diagramDefinitions);
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
// Through this host's own logger, so a request is logged by the host that served it rather than
// by whichever host happens to own Log.Logger when it completes.
app.UseSerilogRequestLogging(options => options.Logger = hostLogger);

// DefaultEnabled so every mapped gRPC service (including DiagramService once that spec
// implements it) accepts grpc-web without needing an explicit .EnableGrpcWeb() call.
app.UseGrpcWeb(new GrpcWebOptions { DefaultEnabled = true });

app.MapGrpcService<AuthenticationService>();
app.MapGrpcService<ProjectService>();
app.MapGrpcService<HierarchyService>();
app.MapGrpcService<ContextService>();
app.MapGrpcService<DiagramService>();
app.MapGrpcService<EditorService>();
app.MapGrpcService<DesignerService>();
app.MapGrpcService<WorkspaceService>();

app.MapClientApp();

// Through ForContext<Program> rather than the bare Log, so this line carries a SourceContext
// like every other one and does not read as coming from nowhere.
Log.ForContext<Program>().Information(
    "ADP is starting in the {Environment} environment with {DiagramTypeCount} diagram types",
    app.Environment.EnvironmentName,
    diagramDefinitions.Count);

// No Log.CloseAndFlush() after this. It closed the GLOBAL logger, which in a test process is
// whichever host started last, not this one. Buffered sinks still get their tail: the host owns
// its logger (preserveStaticLogger above) and disposes it, flushing, when app.Run() disposes the host.
app.Run();

// Exposes the top-level-statement Program class to EtAlii.Adp.Backend.Tests'
// WebApplicationFactory<Program>-based integration test.
[UsedImplicitly]
public partial class Program;

/// <summary>
/// Which host's pipeline <see cref="Log.Logger"/> points at, when more than one host runs in a
/// process - the integration tests start dozens. Production runs one host, and for it this is
/// exactly the old behaviour: bootstrap logger, then the pipeline, then silence at shutdown.
/// </summary>
/// <remarks>
/// A host installs its logger and releases it; it never closes the global. Releasing hands the
/// global to the newest host still live, or back to what was there before any host, and only when
/// the global is still the released one - a
/// plain "restore what was there before" would hand back a host that had meanwhile stopped. One
/// lock, because hosts start and stop on parallel test threads. Without this, a call-site logger
/// in a live host read a silent global after any other host stopped, and a static logger first
/// initialised in that moment stayed silent for the process: both were seen in field logs of the
/// 0x80070497 publish race, as a failure whose record never arrived.
/// </remarks>
internal static class HostLoggers
{
    private static readonly List<Serilog.ILogger> _live = [];

    /// <summary>
    /// What <see cref="Log.Logger"/> was before the first live host replaced it, handed back when
    /// the last one stops: in production Serilog's unset logger, exactly as CloseAndFlush left it;
    /// in a test assembly LogCapture's pipeline, which closing the global used to kill for every
    /// test that ran afterwards.
    /// </summary>
    private static Serilog.ILogger? _outside;

    public static void InstallBootstrapIfNoneLive(Func<Serilog.ILogger> bootstrap)
    {
        lock (_live)
        {
            if (_live.Count == 0)
            {
                // Kept across a host that fails to build, so its bootstrap is never mistaken for it.
                _outside ??= Log.Logger;
                Log.Logger = bootstrap();
            }
        }
    }

    public static IDisposable Install(Serilog.ILogger host)
    {
        lock (_live)
        {
            _live.Add(host);
            Log.Logger = host;
        }

        return new Installed(host);
    }

    private static void Release(Serilog.ILogger host)
    {
        lock (_live)
        {
            if (!_live.Remove(host))
            {
                return;
            }

            var next = _live.Count > 0 ? _live[^1] : _outside ?? Serilog.Core.Logger.None;
            if (_live.Count == 0)
            {
                _outside = null;
            }

            if (ReferenceEquals(Log.Logger, host))
            {
                Log.Logger = next;
            }
        }
    }

    /// <summary>Released at most once, whichever of ApplicationStopped and the <c>using</c> comes first.</summary>
    private sealed class Installed(Serilog.ILogger host) : IDisposable
    {
        public void Dispose() => Release(host);
    }
}
