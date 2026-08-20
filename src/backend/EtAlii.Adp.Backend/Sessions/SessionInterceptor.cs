using Grpc.Core;
using Grpc.Core.Interceptors;

namespace EtAlii.Adp.Backend.Sessions;

/// <summary>
/// Single enforcement point for Requirement 1.6 / grpc-core-communication Requirement 5.1:
/// rejects any call lacking a valid session token, except AuthenticationService.Login itself
/// (which is how a token is first obtained).
/// </summary>
public sealed class SessionInterceptor : Interceptor
{
    public const string SessionTokenMetadataKey = "session-token";

    private readonly ISessionStore _sessionStore;

    public SessionInterceptor(ISessionStore sessionStore)
    {
        _sessionStore = sessionStore;
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

    private static bool IsExempt(string method) =>
        method.EndsWith("/Login", StringComparison.Ordinal);

    private void EnsureAuthenticatedUnlessExempt(ServerCallContext context)
    {
        if (IsExempt(context.Method))
        {
            return;
        }

        var token = context.RequestHeaders.GetValue(SessionTokenMetadataKey);
        if (token is null || !_sessionStore.TryValidate(token, out var userId))
        {
            throw new RpcException(new Status(StatusCode.Unauthenticated, "Missing or invalid session token."));
        }

        SessionContext.SetUserId(context, userId);
    }
}
