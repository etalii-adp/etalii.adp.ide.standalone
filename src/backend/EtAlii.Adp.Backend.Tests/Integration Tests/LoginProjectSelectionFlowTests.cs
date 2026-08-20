using EtAlii.Adp.Backend.Projects;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Backend.Tests.Integration;

/// <summary>
/// Exercises the full login-project-selection flow against the real backend
/// host running in-process (per the F5/local-only philosophy, no external
/// infrastructure), per design.md's Integration Testing strategy.
///
/// AdpService (grpc-core-communication) is not yet implemented, so session
/// consistency is verified across AuthenticationService/ProjectService only;
/// extend this once that spec adds a real AdpService implementation.
/// </summary>
public class LoginProjectSelectionFlowTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private const string DeveloperUsername = "developer";
    private const string DeveloperCredential = "developer";
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
        });
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

        var emptyList = await projectClient.ListProjectsAsync(new ListProjectsRequest(), headers);
        Assert.Empty(emptyList.Projects);

        var pathMessage = new Path();
        pathMessage.Segments.AddRange(
            _sampleProjectFolder.Split(IoPath.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries));

        var addResponse = await projectClient.AddProjectAsync(new AddProjectRequest { Path = pathMessage }, headers);
        Assert.Equal(AddProjectResponse.ResultOneofCase.Added, addResponse.ResultCase);

        var afterAdd = await projectClient.ListProjectsAsync(new ListProjectsRequest(), headers);
        var added = Assert.Single(afterAdd.Projects);
        Assert.Equal("sample-project", added.Name); // "selecting" it is just handing this record to the client's shell

        await projectClient.RemoveProjectAsync(new RemoveProjectRequest { ProjectId = added.Id }, headers);

        var afterRemove = await projectClient.ListProjectsAsync(new ListProjectsRequest(), headers);
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
        await projectClient.ListProjectsAsync(new ListProjectsRequest(), headers);
        await projectClient.ListProjectsAsync(new ListProjectsRequest(), headers);

        await authClient.LogoutAsync(new LogoutRequest { Session = new SessionToken { Value = token } }, headers);

        var exception = await Assert.ThrowsAsync<RpcException>(
            () => projectClient.ListProjectsAsync(new ListProjectsRequest(), headers).ResponseAsync);
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
        });

        Assert.Equal(LoginResponse.ResultOneofCase.Error, response.ResultCase);
    }
}
