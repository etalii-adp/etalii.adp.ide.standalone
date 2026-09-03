using System.Reflection;
using EtAlii.Adp.Backend.Sessions;
using Grpc.Core;
using Serilog;

namespace EtAlii.Adp.Backend.Authentication;

public sealed class AuthenticationService : EtAlii.Adp.AuthenticationService.AuthenticationServiceBase
{
    private static readonly ILogger _logger = Log.ForContext<AuthenticationService>();

    // The one product version, read once: what Nerdbank.GitVersioning stamped into this very
    // assembly (semver plus git height, commit id embedded). Never configuration - a stamped
    // binary cannot disagree with itself (github-build-pipeline Requirement 2.3).
    private static readonly string _productVersion =
        typeof(AuthenticationService).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "";

    private readonly IAuthenticator _authenticator;
    private readonly ISessionStore _sessionStore;

    public AuthenticationService(IAuthenticator authenticator, ISessionStore sessionStore)
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

    public override Task<DescribeProductResponse> DescribeProduct(DescribeProductRequest request, ServerCallContext context)
    {
        // Debug: issued on every login-page load, but the one line that correlates a client's
        // session with the exact binary that served it.
        _logger.Debug("Described the product as {Version}", _productVersion);
        return Task.FromResult(new DescribeProductResponse { Version = _productVersion });
    }
}
