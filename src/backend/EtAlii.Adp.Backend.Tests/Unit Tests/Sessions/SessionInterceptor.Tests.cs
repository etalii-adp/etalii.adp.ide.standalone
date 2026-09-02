using EtAlii.Adp.Backend.Sessions;
using Grpc.Core;
using Grpc.Core.Testing;
using Xunit;

namespace EtAlii.Adp.Backend.Tests;

public class SessionInterceptorTests
{
    private static ServerCallContext CreateContext(string method, Metadata? requestHeaders = null)
    {
        return TestServerCallContext.Create(
            method: method,
            host: "localhost",
            deadline: DateTime.UtcNow.AddMinutes(1),
            requestHeaders: requestHeaders ?? new Metadata(),
            cancellationToken: TestContext.Current.CancellationToken,
            peer: "test-peer",
            authContext: null,
            contextPropagationToken: null,
            writeHeadersFunc: _ => Task.CompletedTask,
            writeOptionsGetter: () => WriteOptions.Default,
            writeOptionsSetter: _ => { });
    }

    private static Task<string> Handler(string request, ServerCallContext context) =>
        Task.FromResult(request);

    [Fact]
    public async Task UnaryServerHandler_LoginMethod_IsExemptEvenWithoutToken()
    {
        // Arrange.
        var interceptor = new SessionInterceptor(new InMemorySessionStore());
        var context = CreateContext("/etalii.adp.AuthenticationService/Login");

        // Act.
        var result = await interceptor.UnaryServerHandler("ping", context, Handler);

        // Assert.
        Assert.Equal("ping", result);
    }

    [Fact]
    public async Task UnaryServerHandler_DescribeProductMethod_IsExemptEvenWithoutToken()
    {
        // Arrange: the login page names the product version before any session exists
        // (github-build-pipeline R3.2) - the exemption is the wire that makes that possible.
        var interceptor = new SessionInterceptor(new InMemorySessionStore());
        var context = CreateContext("/etalii.adp.AuthenticationService/DescribeProduct");

        // Act.
        var result = await interceptor.UnaryServerHandler("ping", context, Handler);

        // Assert.
        Assert.Equal("ping", result);
    }

    [Fact]
    public async Task UnaryServerHandler_WithoutToken_ThrowsUnauthenticated()
    {
        // Arrange.
        var interceptor = new SessionInterceptor(new InMemorySessionStore());
        var context = CreateContext("/etalii.adp.ProjectService/ListProjects");

        // Act.
        var exception = await Assert.ThrowsAsync<RpcException>(
            () => interceptor.UnaryServerHandler("ping", context, Handler));

        // Assert.
        Assert.Equal(StatusCode.Unauthenticated, exception.StatusCode);
    }

    [Fact]
    public async Task UnaryServerHandler_WithInvalidToken_ThrowsUnauthenticated()
    {
        // Arrange and act.
        var interceptor = new SessionInterceptor(new InMemorySessionStore());
        var headers = new Metadata { { SessionInterceptor.SessionTokenMetadataKey, "not-a-real-token" } };
        var context = CreateContext("/etalii.adp.ProjectService/ListProjects", headers);

        // Assert.
        await Assert.ThrowsAsync<RpcException>(
            () => interceptor.UnaryServerHandler("ping", context, Handler));
    }

    [Fact]
    public async Task UnaryServerHandler_WithValidToken_PassesThroughAndSetsUserId()
    {
        // Arrange.
        var userId = ShortGuid.FromName("developer");
        var sessionStore = new InMemorySessionStore();
        var token = sessionStore.Issue(userId);
        var interceptor = new SessionInterceptor(sessionStore);
        var headers = new Metadata { { SessionInterceptor.SessionTokenMetadataKey, token } };
        var context = CreateContext("/etalii.adp.ProjectService/ListProjects", headers);

        // Act.
        var result = await interceptor.UnaryServerHandler("ping", context, Handler);

        // Assert.
        Assert.Equal("ping", result);
        Assert.Equal(userId, SessionContext.GetUserId(context));
    }
}
