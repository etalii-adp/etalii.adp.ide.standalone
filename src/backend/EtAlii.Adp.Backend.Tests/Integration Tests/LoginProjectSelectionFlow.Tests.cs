using EtAlii.Adp.Backend.Projects;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Backend.Tests;

/// <summary>
/// Exercises the full login-project-selection flow against the real backend
/// host running in-process (per the F5/local-only philosophy, no external
/// infrastructure), per design.md's Integration Testing strategy.
///
/// DiagramService (grpc-core-communication) is not yet implemented, so session
/// consistency is verified across AuthenticationService/ProjectService only;
/// extend this once that spec adds a real DiagramService implementation.
/// </summary>
public class LoginProjectSelectionFlowTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private const string DeveloperUsername = "admin";
    private const string DeveloperCredential = "changeme";
    private const string SessionTokenHeader = "session-token";

    private readonly WebApplicationFactory<Program> _factory;
    private readonly string _appDataRoot;
    private readonly string _sampleProjectFolder;

    public LoginProjectSelectionFlowTests(WebApplicationFactory<Program> baseFactory)
    {
        _appDataRoot = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.IntegrationTests", Guid.NewGuid().ToString("N"));
        _sampleProjectFolder = IoPath.Combine(_appDataRoot, "sample-project");
        Directory.CreateDirectory(_sampleProjectFolder);

        _factory = baseFactory.WithWebHostBuilder(builder =>
        {
            // appsettings.developer.json supplies the local-mode credential (task 9).
            builder.UseEnvironment("developer");
            builder.ConfigureServices(services =>
            {
                // Isolate each test run's persisted project list from the real
                // ApplicationData folder and from other test runs.
                services.RemoveAll<IProjectStore>();
                services.AddSingleton<IProjectStore>(new FileProjectStore(_appDataRoot));
                // The problem cache must live and die with this test, not in the real user
                // profile the host's AddProblems registration points at (found by the
                // errors-and-warnings-panel manual pass: every run left a cache file behind).
                services.RemoveAll<Problems.IProblemStore>();
                services.AddSingleton<Problems.IProblemStore>(provider => new Problems.ProblemStore(
                    _appDataRoot,
                    provider.GetRequiredService<Hierarchy.DiagramFileRouter>(),
                    provider.GetRequiredService<Diagram.DiagramValidators>()));
            });
        });
    }

    public void Dispose()
    {
        _factory.Dispose();
        if (Directory.Exists(_appDataRoot))
        {
            Directory.Delete(_appDataRoot, recursive: true);
        }
    }

    private GrpcChannel CreateChannel()
    {
        var httpClient = _factory.CreateDefaultClient();
        return GrpcChannel.ForAddress(httpClient.BaseAddress!, new GrpcChannelOptions { HttpClient = httpClient });
    }

    private static async Task<string> LoginAsDeveloperAsync(AuthenticationService.AuthenticationServiceClient authClient)
    {
        var response = await authClient.LoginAsync(new LoginRequest
        {
            Username = DeveloperUsername,
            Credential = DeveloperCredential,
        }, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(LoginResponse.ResultOneofCase.Session, response.ResultCase);
        return response.Session.Value;
    }

    [Fact]
    public async Task FullFlow_LoginThenAddSelectRemove_Works()
    {
        using var channel = CreateChannel();
        var authClient = new AuthenticationService.AuthenticationServiceClient(channel);
        var projectClient = new ProjectService.ProjectServiceClient(channel);

        var token = await LoginAsDeveloperAsync(authClient);
        var headers = new Metadata { { SessionTokenHeader, token } };

        var emptyList = await projectClient.ListProjectsAsync(new ListProjectsRequest(), headers, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Empty(emptyList.Projects);

        var pathMessage = new Path();
        pathMessage.Segments.AddRange(
            _sampleProjectFolder.Split(IoPath.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries));

        var addResponse = await projectClient.AddProjectAsync(new AddProjectRequest { Path = pathMessage }, headers, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(AddProjectResponse.ResultOneofCase.Added, addResponse.ResultCase);

        var afterAdd = await projectClient.ListProjectsAsync(new ListProjectsRequest(), headers, cancellationToken: TestContext.Current.CancellationToken);
        var added = Assert.Single(afterAdd.Projects);
        Assert.Equal("sample-project", added.Name); // "selecting" it is just handing this record to the client's shell

        await projectClient.RemoveProjectAsync(new RemoveProjectRequest { ProjectId = added.Id }, headers, cancellationToken: TestContext.Current.CancellationToken);

        var afterRemove = await projectClient.ListProjectsAsync(new ListProjectsRequest(), headers, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Empty(afterRemove.Projects);
    }

    [Fact]
    public async Task Session_PersistsAcrossAuthenticationAndProjectService_AndIsRejectedAfterLogout()
    {
        using var channel = CreateChannel();
        var authClient = new AuthenticationService.AuthenticationServiceClient(channel);
        var projectClient = new ProjectService.ProjectServiceClient(channel);

        var token = await LoginAsDeveloperAsync(authClient);
        var headers = new Metadata { { SessionTokenHeader, token } };

        // Same token, multiple calls, no re-authentication needed.
        await projectClient.ListProjectsAsync(new ListProjectsRequest(), headers, cancellationToken: TestContext.Current.CancellationToken);
        await projectClient.ListProjectsAsync(new ListProjectsRequest(), headers, cancellationToken: TestContext.Current.CancellationToken);

        await authClient.LogoutAsync(new LogoutRequest { Session = new SessionToken { Value = token } }, headers, cancellationToken: TestContext.Current.CancellationToken);

        var exception = await Assert.ThrowsAsync<RpcException>(
            () => projectClient.ListProjectsAsync(new ListProjectsRequest(), headers, cancellationToken: TestContext.Current.CancellationToken).ResponseAsync);
        Assert.Equal(StatusCode.Unauthenticated, exception.StatusCode);
    }

    [Fact]
    public async Task InvalidCredentials_AreRejectedWithoutIssuingASession()
    {
        using var channel = CreateChannel();
        var authClient = new AuthenticationService.AuthenticationServiceClient(channel);

        var response = await authClient.LoginAsync(new LoginRequest
        {
            Username = DeveloperUsername,
            Credential = "not-the-right-credential",
        }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(LoginResponse.ResultOneofCase.Error, response.ResultCase);
    }
}
