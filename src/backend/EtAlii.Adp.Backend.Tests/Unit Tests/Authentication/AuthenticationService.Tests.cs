using EtAlii.Adp.Backend.Authentication;
using EtAlii.Adp.Backend.Sessions;
using Grpc.Core;
using Grpc.Core.Testing;
using Xunit;

namespace EtAlii.Adp.Backend.Tests;

/// <summary>
/// The product-description answer (github-build-pipeline Requirement 2.3): the version comes
/// from the assembly Nerdbank.GitVersioning stamped, never from configuration, so it cannot
/// disagree with the binary actually running.
/// </summary>
public class AuthenticationServiceTests
{
    private sealed class RefusingAuthenticator : IAuthenticator
    {
        public bool Validate(string username, string credential) => false;
    }

    private static ServerCallContext CreateContext() =>
        TestServerCallContext.Create(
            method: "/etalii.adp.AuthenticationService/DescribeProduct",
            host: "localhost",
            deadline: DateTime.UtcNow.AddMinutes(1),
            requestHeaders: new Metadata(),
            cancellationToken: TestContext.Current.CancellationToken,
            peer: "test-peer",
            authContext: null,
            contextPropagationToken: null,
            writeHeadersFunc: _ => Task.CompletedTask,
            writeOptionsGetter: () => WriteOptions.Default,
            writeOptionsSetter: _ => { });

    [Fact]
    public async Task DescribeProduct_AnswersTheStampedInformationalVersion()
    {
        // Arrange: the authenticator is irrelevant to this call - a refusing one proves it.
        var service = new Backend.Authentication.AuthenticationService(new RefusingAuthenticator(), new InMemorySessionStore());

        // Act.
        var response = await service.DescribeProduct(new DescribeProductRequest(), CreateContext());

        // Assert: semver-shaped and non-empty - the informational version NB.GV stamps
        // (semver plus height, commit id embedded on non-release builds).
        Assert.Matches(@"^\d+\.\d+", response.Version);
    }
}
