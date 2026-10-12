using EtAlii.Adp.Authentication.Wire;
using EtAlii.Adp.Projects.Wire;
using EtAlii.Adp.TestSupport.Host;
using Grpc.Core;
using Xunit;
using Path = EtAlii.Adp.Documents.Wire.Path;
using ProjectService = EtAlii.Adp.Projects.Wire.ProjectService;

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
public class LoginProjectSelectionFlowTests(HostFixture host) : IClassFixture<HostFixture>
{
    private readonly string _sampleProjectFolder = host.NewProjectFolder();

    private static async Task<string> LoginAsDeveloperAsync(AuthenticationService.AuthenticationServiceClient authClient)
    {
        var response = await authClient.LoginAsync(new LoginRequest
        {
            Username = FlowConstants.DeveloperUsername,
            Credential = FlowConstants.DeveloperCredential,
        }, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(LoginResponse.ResultOneofCase.Session, response.ResultCase);
        return response.Session.Value;
    }

    [Fact]
    public async Task DescribeProduct_AnswersUnauthenticated_WithTheStampedVersion()
    {
        // Arrange: no login, no token - the login page asks before any session exists
        // (github-build-pipeline R2.3, R3.2), so the exemption must hold end-to-end.
        using var channel = host.CreateChannel();
        var authClient = new AuthenticationService.AuthenticationServiceClient(channel);

        // Act.
        var response = await authClient.DescribeProductAsync(new DescribeProductRequest(), cancellationToken: TestContext.Current.CancellationToken);

        // Assert: the Nerdbank.GitVersioning-stamped informational version - semver-shaped,
        // never empty and never a hand-maintained string.
        Assert.Matches(@"^\d+\.\d+", response.Version);
    }

    [Fact]
    public async Task FullFlow_LoginThenAddSelectRemove_Works()
    {
        // Arrange.
        using var channel = host.CreateChannel();
        var authClient = new AuthenticationService.AuthenticationServiceClient(channel);
        var projectClient = new ProjectService.ProjectServiceClient(channel);

        var token = await LoginAsDeveloperAsync(authClient);
        var headers = new Metadata { { FlowConstants.SessionTokenHeader, token } };

        // Act and assert, step by step.
        var emptyList = await projectClient.ListProjectsAsync(new ListProjectsRequest(), headers, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Empty(emptyList.Projects);

        var pathMessage = Path.OfLocation(
            _sampleProjectFolder);

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
        // Arrange.
        using var channel = host.CreateChannel();
        var authClient = new AuthenticationService.AuthenticationServiceClient(channel);
        var projectClient = new ProjectService.ProjectServiceClient(channel);

        var token = await LoginAsDeveloperAsync(authClient);
        var headers = new Metadata { { FlowConstants.SessionTokenHeader, token } };

        // Same token, multiple calls, no re-authentication needed.
        await projectClient.ListProjectsAsync(new ListProjectsRequest(), headers, cancellationToken: TestContext.Current.CancellationToken);
        await projectClient.ListProjectsAsync(new ListProjectsRequest(), headers, cancellationToken: TestContext.Current.CancellationToken);

        await authClient.LogoutAsync(new LogoutRequest { Session = new SessionToken { Value = token } }, headers, cancellationToken: TestContext.Current.CancellationToken);

        // Act and assert, step by step.
        var exception = await Assert.ThrowsAsync<RpcException>(
            () => projectClient.ListProjectsAsync(new ListProjectsRequest(), headers, cancellationToken: TestContext.Current.CancellationToken).ResponseAsync);
        Assert.Equal(StatusCode.Unauthenticated, exception.StatusCode);
    }

    [Fact]
    public async Task InvalidCredentials_AreRejectedWithoutIssuingASession()
    {
        // Arrange.
        using var channel = host.CreateChannel();
        var authClient = new AuthenticationService.AuthenticationServiceClient(channel);

        // Act.
        var response = await authClient.LoginAsync(new LoginRequest
        {
            Username = FlowConstants.DeveloperUsername,
            Credential = "not-the-right-credential",
        }, cancellationToken: TestContext.Current.CancellationToken);

        // Assert.
        Assert.Equal(LoginResponse.ResultOneofCase.Error, response.ResultCase);
    }
}
