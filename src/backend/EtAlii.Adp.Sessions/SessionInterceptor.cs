using Grpc.Core;
using Grpc.Core.Interceptors;
using Serilog;

namespace EtAlii.Adp.Sessions;

/// <summary>
/// Single enforcement point for Requirement 1.6 / grpc-core-communication Requirement 5.1:
/// rejects any call lacking a valid session token, except the calls
/// <see cref="ISessionExemptions"/> names (which are how a token is first obtained).
/// </summary>
public sealed class SessionInterceptor : Interceptor
{
    public const string SessionTokenMetadataKey = "session-token";

    private static readonly ILogger _logger = Log.ForContext<SessionInterceptor>();

    private readonly ISessionStore _sessionStore;
    private readonly ISessionExemptions _exemptions;

    public SessionInterceptor(ISessionStore sessionStore, ISessionExemptions exemptions)
    {
        _sessionStore = sessionStore;
        _exemptions = exemptions;
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

    private void EnsureAuthenticatedUnlessExempt(ServerCallContext context)
    {
        if (_exemptions.IsExempt(context.Method))
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
