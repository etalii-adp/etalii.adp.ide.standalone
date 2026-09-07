using Grpc.Core;
using Grpc.Core.Interceptors;
using Serilog;
#if DEBUG
using Microsoft.Extensions.Options;
#endif

namespace EtAlii.Adp.Authentication;

/// <summary>
/// Single enforcement point for Requirement 1.6 / grpc-core-communication Requirement 5.1:
/// rejects any call lacking a valid session token, except AuthenticationService.Login itself
/// (which is how a token is first obtained).
/// </summary>
public sealed class SessionInterceptor : Interceptor
{
    public const string SessionTokenMetadataKey = "session-token";

    private static readonly ILogger _logger = Log.ForContext<SessionInterceptor>();

    private readonly ISessionStore _sessionStore;
#if DEBUG
    private readonly LocalAuthenticatorOptions _localAuthenticator;
#endif

    public SessionInterceptor(
        ISessionStore sessionStore
#if DEBUG
        ,
        // Optional so the existing tests that construct an interceptor with a store alone keep
        // compiling and keep meaning what they meant; absent, the bypass is not disabled, which
        // is its ordinary Debug state.
        IOptions<LocalAuthenticatorOptions>? localAuthenticator = null
#endif
        )
    {
        _sessionStore = sessionStore;
#if DEBUG
        _localAuthenticator = localAuthenticator?.Value ?? new LocalAuthenticatorOptions();
#endif
    }

    public override Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
        TRequest request,
        ServerCallContext context,
        UnaryServerMethod<TRequest, TResponse> continuation)
    {
        EnsureAuthenticatedUnlessExempt(context);
        return continuation(request, context);
    }

    public override Task DuplexStreamingServerHandler<TRequest, TResponse>(
        IAsyncStreamReader<TRequest> requestStream,
        IServerStreamWriter<TResponse> responseStream,
        ServerCallContext context,
        DuplexStreamingServerMethod<TRequest, TResponse> continuation)
    {
        EnsureAuthenticatedUnlessExempt(context);
        return continuation(requestStream, responseStream, context);
    }

    public override Task ServerStreamingServerHandler<TRequest, TResponse>(
        TRequest request,
        IServerStreamWriter<TResponse> responseStream,
        ServerCallContext context,
        ServerStreamingServerMethod<TRequest, TResponse> continuation)
    {
        EnsureAuthenticatedUnlessExempt(context);
        return continuation(request, responseStream, context);
    }

    /// <summary>Whether a call may proceed without a session token at all.</summary>
    /// <remarks>
    /// Internal rather than private so the tests can ask it directly: whether a route is
    /// reachable without a credential is the single most consequential line in this file, and
    /// it deserves to be asserted rather than inferred from a call that happened to succeed.
    /// </remarks>
    internal bool IsExempt(string method) =>
        // Login is how a token is first obtained; DescribeProduct is how the login page names
        // the product version before any session exists - a version is not a secret, and the
        // page that shows it renders before anyone can log in (github-build-pipeline R3.2).
        method.EndsWith("/Login", StringComparison.Ordinal)
        || method.EndsWith("/DescribeProduct", StringComparison.Ordinal)
#if DEBUG
        // The developer sign-in bypass (developer-sign-in-bypass Requirement 2.1). This entry
        // and the handler it exempts are the whole of the bypass, and each sits inside a
        // #if DEBUG in its own file so that neither can survive into a release without the
        // other. The configuration check is repeated here rather than left to the handler:
        // turning the bypass off should close the route, not merely make the route answer
        // nothing.
        || (!_localAuthenticator.DeveloperSessionDisabled
            && method.EndsWith("/DeveloperSession", StringComparison.Ordinal))
#endif
        ;

    private void EnsureAuthenticatedUnlessExempt(ServerCallContext context)
    {
        if (IsExempt(context.Method))
        {
            return;
        }

        var token = context.RequestHeaders.GetValue(SessionTokenMetadataKey);
        if (token is null || !_sessionStore.TryValidate(token, out var userId))
        {
            // Which call was refused and whether a token was even offered - enough to tell a
            // client that never logged in from one whose session expired, without recording
            // the token itself.
            _logger.Warning(
                "Rejecting {Method}: {TokenState} session token",
                context.Method,
                token is null ? "no" : "an invalid");
            throw new RpcException(new Status(StatusCode.Unauthenticated, "Missing or invalid session token."));
        }

        _logger.Verbose("{Method} authenticated as {UserId}", context.Method, userId);
        SessionContext.SetUserId(context, userId);
    }
}
