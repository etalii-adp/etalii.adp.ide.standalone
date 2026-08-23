using EtAlii.Adp.Backend.Sessions;
using Grpc.Core;
using Serilog;

namespace EtAlii.Adp.Backend.Authentication;

public sealed class AuthenticationServiceImpl : AuthenticationService.AuthenticationServiceBase
{
    private static readonly ILogger _logger = Log.ForContext<AuthenticationServiceImpl>();

    private readonly IAuthenticator _authenticator;
    private readonly ISessionStore _sessionStore;

    public AuthenticationServiceImpl(IAuthenticator authenticator, ISessionStore sessionStore)
    {
        _authenticator = authenticator;
        _sessionStore = sessionStore;
    }

    public override Task<LoginResponse> Login(LoginRequest request, ServerCallContext context)
    {
        if (!_authenticator.Validate(request.Username, request.Credential))
        {
            // Warning, not Information: a rejected login is what a brute-force attempt looks
            // like from here. The username is logged, never the credential.
            _logger.Warning("Login rejected for {Username}: the username or credential did not match", request.Username);
            return Task.FromResult(new LoginResponse
            {
                Error = new LoginError { Message = "Invalid username or credential." }
            });
        }

        var userId = ShortGuid.FromName(request.Username);
        var token = _sessionStore.Issue(userId);
        // The user id, never the token: the token is a bearer credential for the whole session.
        _logger.Information("Login accepted for {Username}, issued a session for {UserId}", request.Username, userId);
        return Task.FromResult(new LoginResponse
        {
            Session = new SessionToken { Value = token }
        });
    }

    public override Task<LogoutResponse> Logout(LogoutRequest request, ServerCallContext context)
    {
        _sessionStore.Revoke(request.Session.Value);
        _logger.Information("Session revoked for {UserId}", SessionContext.GetUserId(context));
        return Task.FromResult(new LogoutResponse());
    }
}
