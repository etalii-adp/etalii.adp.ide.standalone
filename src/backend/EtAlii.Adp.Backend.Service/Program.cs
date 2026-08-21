using EtAlii.Adp.Backend.Authentication;
using EtAlii.Adp.Backend.Client;
using EtAlii.Adp.Backend.Hierarchy;
using EtAlii.Adp.Backend.Projects;
using EtAlii.Adp.Backend.Sessions;
using JetBrains.Annotations;

var builder = WebApplication.CreateBuilder(args);

var localAuthenticationOptionsSection = builder.Configuration.GetSection(LocalAuthenticatorOptions.SectionName);
builder.Services.Configure<LocalAuthenticatorOptions>(localAuthenticationOptionsSection);
builder.Services.AddSingleton<IAuthenticator, LocalAuthenticator>();
builder.Services.AddSingleton<ISessionStore, InMemorySessionStore>();
builder.Services.AddSingleton<IProjectStore>(_ => new FileProjectStore(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)));
builder.Services.AddSingleton<IHierarchyModelStore, HierarchyModelStore>();

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

// DefaultEnabled so every mapped gRPC service (including AdpService once that spec
// implements it) accepts grpc-web without needing an explicit .EnableGrpcWeb() call.
app.UseGrpcWeb(new GrpcWebOptions { DefaultEnabled = true });

app.MapGrpcService<AuthenticationServiceImpl>();
app.MapGrpcService<ProjectServiceImpl>();
app.MapGrpcService<HierarchyServiceImpl>();
// AdpService (grpc-core-communication) is mapped here once that spec implements it.

app.MapClientApp();

app.Run();

// Exposes the top-level-statement Program class to EtAlii.Adp.Backend.Tests'
// WebApplicationFactory<Program>-based integration test.
[UsedImplicitly]
public partial class Program;
