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
            cancellationToken: CancellationToken.None,
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
        var interceptor = new SessionInterceptor(new InMemorySessionStore());
        var context = CreateContext("/etalii.adp.AuthenticationService/Login");

        var result = await interceptor.UnaryServerHandler("ping", context, Handler);

        Assert.Equal("ping", result);
    }

    [Fact]
    public async Task UnaryServerHandler_WithoutToken_ThrowsUnauthenticated()
    {
        var interceptor = new SessionInterceptor(new InMemorySessionStore());
        var context = CreateContext("/etalii.adp.ProjectService/ListProjects");

        var exception = await Assert.ThrowsAsync<RpcException>(
            () => interceptor.UnaryServerHandler("ping", context, Handler));

        Assert.Equal(StatusCode.Unauthenticated, exception.StatusCode);
    }

    [Fact]
    public async Task UnaryServerHandler_WithInvalidToken_ThrowsUnauthenticated()
    {
        var interceptor = new SessionInterceptor(new InMemorySessionStore());
        var headers = new Metadata { { SessionInterceptor.SessionTokenMetadataKey, "not-a-real-token" } };
        var context = CreateContext("/etalii.adp.ProjectService/ListProjects", headers);

        await Assert.ThrowsAsync<RpcException>(
            () => interceptor.UnaryServerHandler("ping", context, Handler));
    }

    [Fact]
    public async Task UnaryServerHandler_WithValidToken_PassesThroughAndSetsUsername()
    {
        var sessionStore = new InMemorySessionStore();
        var token = sessionStore.Issue("developer");
        var interceptor = new SessionInterceptor(sessionStore);
        var headers = new Metadata { { SessionInterceptor.SessionTokenMetadataKey, token } };
        var context = CreateContext("/etalii.adp.ProjectService/ListProjects", headers);

        var result = await interceptor.UnaryServerHandler("ping", context, Handler);

        Assert.Equal("ping", result);
        Assert.Equal("developer", SessionContext.GetUsername(context));
    }
}
