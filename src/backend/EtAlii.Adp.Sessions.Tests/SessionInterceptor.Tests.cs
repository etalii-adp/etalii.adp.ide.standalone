using Grpc.Core;
using Grpc.Core.Testing;
using Xunit;

namespace EtAlii.Adp.Sessions.Tests;

public class SessionInterceptorTests
{
    private const string ExemptMethod = "/etalii.adp.AuthenticationService/Login";
    private const string GuardedMethod = "/etalii.adp.ProjectService/ListProjects";

    /// <summary>Exempts the one method it is given; which calls are exempt is the authentication's to say.</summary>
    private sealed class OneExemption(string method) : ISessionExemptions
    {
        public bool IsExempt(string calledMethod) => calledMethod == method;
    }

    private static SessionInterceptor CreateInterceptor(ISessionStore sessionStore) =>
        new(sessionStore, new OneExemption(ExemptMethod));

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
    public async Task UnaryServerHandler_ExemptMethod_PassesWithoutToken()
    {
        // Arrange.
        var interceptor = CreateInterceptor(new InMemorySessionStore());
        var context = CreateContext(ExemptMethod);

        // Act.
        var result = await interceptor.UnaryServerHandler("ping", context, Handler);

        // Assert.
        Assert.Equal("ping", result);
    }

    [Fact]
    public async Task UnaryServerHandler_WithoutToken_ThrowsUnauthenticated()
    {
        // Arrange.
        var interceptor = CreateInterceptor(new InMemorySessionStore());
        var context = CreateContext(GuardedMethod);

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
        var interceptor = CreateInterceptor(new InMemorySessionStore());
        var headers = new Metadata { { SessionInterceptor.SessionTokenMetadataKey, "not-a-real-token" } };
        var context = CreateContext(GuardedMethod, headers);

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
        var interceptor = CreateInterceptor(sessionStore);
        var headers = new Metadata { { SessionInterceptor.SessionTokenMetadataKey, token } };
        var context = CreateContext(GuardedMethod, headers);

        // Act.
        var result = await interceptor.UnaryServerHandler("ping", context, Handler);

        // Assert.
        Assert.Equal("ping", result);
        Assert.Equal(userId, SessionContext.GetUserId(context));
    }
}
