using EtAlii.Adp.Backend.Authentication;
using EtAlii.Adp.Backend.Client;
using EtAlii.Adp.Backend.Projects;
using EtAlii.Adp.Backend.Sessions;
using JetBrains.Annotations;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<LocalAuthenticatorOptions>(
    builder.Configuration.GetSection(LocalAuthenticatorOptions.SectionName));
builder.Services.AddSingleton<IAuthenticator, LocalAuthenticator>();
builder.Services.AddSingleton<ISessionStore, InMemorySessionStore>();
builder.Services.AddSingleton<IProjectStore>(_ =>
    new FileProjectStore(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)));

builder.Services.Configure<ClientAppOptions>(
    builder.Configuration.GetSection(ClientAppOptions.SectionName));
builder.Services.AddClientAppHosting();

builder.Services.AddGrpc(options =>
{
    // Applied to every gRPC call; SessionInterceptor itself exempts
    // AuthenticationService.Login (Requirement 1.6).
    options.Interceptors.Add<SessionInterceptor>();
});

var app = builder.Build();

app.MapGrpcService<AuthenticationServiceImpl>();
app.MapGrpcService<ProjectServiceImpl>();
// AdpService (grpc-core-communication) is mapped here once that spec implements it.

app.MapClientApp();

app.Run();

// Exposes the top-level-statement Program class to EtAlii.Adp.Backend.Tests'
// WebApplicationFactory<Program>-based integration test.
[UsedImplicitly]
public partial class Program;
