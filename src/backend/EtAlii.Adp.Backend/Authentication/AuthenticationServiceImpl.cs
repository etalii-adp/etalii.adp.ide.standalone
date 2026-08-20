using EtAlii.Adp.Backend.Sessions;
using Grpc.Core;

namespace EtAlii.Adp.Backend.Authentication;

public sealed class AuthenticationServiceImpl : AuthenticationService.AuthenticationServiceBase
{
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
            return Task.FromResult(new LoginResponse
            {
                Error = new LoginError { Message = "Invalid username or credential." }
            });
        }

        var token = _sessionStore.Issue(request.Username);
        return Task.FromResult(new LoginResponse
        {
            Session = new SessionToken { Value = token }
        });
    }

    public override Task<LogoutResponse> Logout(LogoutRequest request, ServerCallContext context)
    {
        _sessionStore.Revoke(request.Session.Value);
        return Task.FromResult(new LogoutResponse());
    }
}
